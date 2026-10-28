using System.Diagnostics;

namespace MobileJourneys.Framework;

internal static class JourneyRunner
{
	public static JourneyResult Run(
		TestDriver driver,
		int worker,
		TestCase testCase,
		ScreenshotManager manager,
		RunReporter reporter,
		CancellationToken cancellationToken
	)
	{
		var journey = testCase.Journey;
		var config = testCase.Config;
		var stopwatch = Stopwatch.StartNew();
		reporter.JourneyStarting(config, worker, journey.Name);

		var totalSteps = journey.Steps.Length + 1;

		// Starting the journey is the first step's action, so a journey that cannot start fails on that
		// step and leaves its failure artifacts, which are all the viewer and --rerun see.
		var steps = new List<(
			int Number,
			string Name,
			Action Execute,
			string[] MaskElements,
			bool PrefetchMasks,
			JourneyStep? Step
		)>
		{
			(
				1,
				journey.InitialName,
				() =>
				{
					var deviceId = driver.GetDeviceId();
					config.SetSystemFontSize(deviceId, SystemFontSize.Large);
					config.SetSystemTheme(deviceId, config.IsLightTheme);
					var prepared = driver.Backend?.PrepareFor(journey.Scenario) ?? journey.Scenario;
					driver.ClearAppLogs();
					driver.RelaunchApp(prepared);
					ProcessExpectations(driver, journey.InitialExpect);
				},
				journey.InitialMaskElements ?? [],
				false,
				null
			),
		};
		for (var i = 0; i < journey.Steps.Length; i++)
		{
			var step = journey.Steps[i];
			steps.Add(
				(
					steps.Count + 1,
					step.Name,
					() =>
					{
						step.Action.Execute(driver);
						ProcessExpectations(driver, step.Expect ?? []);
					},
					step.MaskElements ?? [],
					step.PrefetchMasks,
					step
				)
			);
		}

		// A step whose screenshot differs leaves the journey on the screen it expected, so the steps
		// after it still run and each says what it found. A step that threw has lost the app: the
		// screen it left is not the one the next step expects, so every step after it would fail too,
		// each spending the whole wait budget on something that is not coming.
		var failures = new List<JourneyFailureException>();
		var stepsRun = 0;
		foreach (var (number, name, execute, maskElements, prefetchMasks, journeyStep) in steps)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var testStep = new TestStep(
				driver.Config,
				journey.ContainerForStep(number),
				JourneyDefinition.FormatStepName(number, name),
				journey.Name
			);
			var stepPassed = true;
			Exception? error = null;
			string? detail = null;
			try
			{
				var stepResult = driver.DoActionAndCompareWithBaseline(execute, testStep, maskElements, prefetchMasks);
				stepPassed = stepResult.Outcome == ScreenshotOutcome.Matched;
				if (!stepPassed)
				{
					detail =
						stepResult.Outcome == ScreenshotOutcome.NoBaseline
							? "no baseline yet — accept the new capture in the viewer"
							: $"screenshots differ {stepResult.PixelDiffPercentage:F2}%";
					var message =
						$"step {number}/{totalSteps}: {name} — {detail}\n" + $"  {stepResult.ReportPath ?? ""}";
					failures.Add(new JourneyFailureException(message, journey, journeyStep, number, totalSteps, name));
				}
			}
			// Once the run is interrupted, a step's exception comes from the interruption: the driver's
			// waits throw to stop it, and the Appium server receives the same Ctrl+C. Recorded as a
			// failure, it would leave artifacts that --rerun then selects the journey on — and the step's
			// artifacts from its last run stay, so an interrupted journey is still selected by them.
			catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
			{
				stepPassed = false;
				error = ex;
				detail = ex.Message;
				manager.DeleteFailureArtifactsForStep(testStep);
				HandleStepFailure(driver, manager, ex, testStep);
				var message = $"step {number}/{totalSteps}: {name} — {ex.Message}";
				failures.Add(new JourneyFailureException(message, journey, journeyStep, number, totalSteps, name, ex));
			}

			stepsRun = number;
			reporter.StepCompleted(testStep, worker, number, totalSteps, name, stepPassed, detail, error);
			if (error is not null)
			{
				break;
			}
		}

		stopwatch.Stop();

		if (failures.Count == 0)
		{
			return new JourneyResult(testCase, true, stopwatch.Elapsed, string.Empty, null);
		}

		var explanation = Explain(failures.Select(failure => failure.Message), totalSteps, stepsRun);
		var firstFailure = failures[0];
		var aggregate =
			failures.Count == 1
				? firstFailure
				: new JourneyFailureException(
					explanation,
					journey,
					firstFailure.Step,
					firstFailure.StepNumber,
					totalSteps,
					firstFailure.StepName,
					firstFailure.InnerException
				);
		return new JourneyResult(testCase, false, stopwatch.Elapsed, explanation, aggregate);
	}

	/// <summary>
	/// Words a journey's failure: a lone step's message as it is, or the failed steps listed under a
	/// count of them, either followed by the steps the journey stopped before.
	/// </summary>
	/// <param name="failureMessages">Each failed step's message, in step order.</param>
	/// <param name="totalSteps">How many steps the journey has.</param>
	/// <param name="stepsRun">How many of them ran before the journey stopped.</param>
	/// <returns>The explanation.</returns>
	internal static string Explain(IEnumerable<string> failureMessages, int totalSteps, int stepsRun)
	{
		var messages = failureMessages.ToList();
		var notRun =
			stepsRun == totalSteps ? string.Empty
			: stepsRun + 1 == totalSteps ? $"\n  step {totalSteps} was not run"
			: $"\n  steps {stepsRun + 1}–{totalSteps} were not run";
		if (messages.Count == 1)
		{
			return messages[0] + notRun;
		}

		var details = string.Join("\n", messages.Select(message => $"  • {message}"));
		return $"{messages.Count} of {stepsRun} steps failed:\n{details}{notRun}";
	}

	private static void ProcessExpectations(TestDriver driver, Expectation[] expectations)
	{
		foreach (var expectation in expectations)
		{
			expectation.Verify(driver);
		}
	}

	private static void HandleStepFailure(TestDriver driver, ScreenshotManager manager, Exception ex, TestStep key)
	{
		// Capture before anything else: querying the app state and pulling a device crash log take
		// seconds, and on a timeout failure the screen often finishes rendering in that window —
		// producing evidence that shows a perfectly good screen.
		var screenshot = driver.TryCaptureScreenshot();
		var details = $"{ex.GetType().FullName}: {ex.Message}";
		var appCrashed = driver.IsAppCrashed();
		if (appCrashed)
		{
			var exceptionLog = driver.CaptureDeviceCrashLog();
			_ = manager.WriteFailScreenshot(key, "CRASH", screenshot, details);

			var logContent =
				exceptionLog
				?? (
					"App process is not running. No crash log found on device.\n\n"
					+ "Diagnostics:\n"
					+ $"- Platform: {driver.Config.Platform}\n"
					+ $"- Device: {driver.Config.DeviceName} ({driver.GetDeviceId()})\n"
					+ "- crash.log: not found via 'adb shell run-as' (check test output for stderr)\n"
					+ "- logcat: no relevant crash output found\n"
					+ $"- Original exception: {ex.GetType().Name}: {ex.Message}"
				);
			manager.WriteCrashLog(key, logContent);
		}
		else
		{
			var sanitized = new string([.. ex.Message.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);
			sanitized = sanitized.Length > 80 ? sanitized[..80] : sanitized;
			_ = manager.WriteFailScreenshot(key, sanitized, screenshot, details);
		}
	}
}
