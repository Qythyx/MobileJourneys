using System.Diagnostics;

namespace MobileJourneys.Framework;

internal static class JourneyRunner
{
	public static async Task<JourneyResult> RunAsync(
		TestDriver driver,
		TestCase testCase,
		MtpReporter reporter,
		ScreenshotManager manager
	)
	{
		var journey = testCase.Journey;
		var config = testCase.Config;
		var deviceId = driver.GetDeviceId();
		var stopwatch = Stopwatch.StartNew();
		config.SetSystemFontSize(deviceId, SystemFontSize.Large);
		config.SetSystemTheme(deviceId, config.IsLightTheme);

		var totalSteps = journey.Steps.Length + 1;

		driver.CurrentJourneyEnv = journey.Scenario.ForFixture(config);
		driver.ClearAppLogs();
		driver.RelaunchApp(driver.CurrentJourneyEnv);

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
				() => ProcessExpectations(driver, journey.InitialExpect),
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

		// Run every step even after one fails, collecting all failures, so a single run surfaces
		// every issue in the journey instead of only the first.
		var failures = new List<JourneyFailureException>();
		foreach (var (number, name, execute, maskElements, prefetchMasks, journeyStep) in steps)
		{
			await reporter.StepStartedAsync(testCase, number, totalSteps, name);
			var numberedStepName = JourneyDefinition.FormatStepName(number, name);
			try
			{
				manager.DeleteFailureArtifactsForStep(new(driver.Config, journey.Name, numberedStepName));
				var stepResult = driver.DoActionAndCompareWithBaseline(
					execute,
					journey.Name,
					numberedStepName,
					maskElements,
					prefetchMasks
				);
				if (!stepResult.Passed)
				{
					var message =
						$"step {number}/{totalSteps}: {name} — screenshots differ {stepResult.PixelDiffPercentage:F2}%\n"
						+ $"  {stepResult.ReportPath ?? ""}";
					failures.Add(new JourneyFailureException(message, journey, journeyStep, number, totalSteps, name));
				}
			}
			catch (Exception ex)
			{
				HandleStepFailure(driver, manager, ex, numberedStepName, journey.Name);
				var message = $"step {number}/{totalSteps}: {name} — {ex.Message}";
				failures.Add(new JourneyFailureException(message, journey, journeyStep, number, totalSteps, name, ex));
			}
		}

		stopwatch.Stop();

		if (failures.Count == 0)
		{
			return new JourneyResult(testCase, true, stopwatch.Elapsed, string.Empty, null);
		}

		if (failures.Count == 1)
		{
			var failure = failures[0];
			return new JourneyResult(testCase, false, stopwatch.Elapsed, failure.Message, failure);
		}

		var summary = BuildFailureSummary(failures, totalSteps);
		var firstFailure = failures[0];
		var aggregate = new JourneyFailureException(
			summary,
			journey,
			firstFailure.Step,
			firstFailure.StepNumber,
			totalSteps,
			firstFailure.StepName
		);
		return new JourneyResult(testCase, false, stopwatch.Elapsed, summary, aggregate);
	}

	private static string BuildFailureSummary(List<JourneyFailureException> failures, int totalSteps)
	{
		var details = string.Join("\n", failures.Select(f => $"  • {f.Message}"));
		return $"{failures.Count} of {totalSteps} steps failed:\n{details}";
	}

	private static void ProcessExpectations(TestDriver driver, Expectation[] expectations)
	{
		foreach (var expectation in expectations)
		{
			expectation.Verify(driver);
		}
	}

	private static void HandleStepFailure(
		TestDriver driver,
		ScreenshotManager manager,
		Exception ex,
		string filePrefix,
		string journeyName
	)
	{
		var key = new TestStep(driver.Config, journeyName, filePrefix);
		var appCrashed = driver.IsAppCrashed();
		if (appCrashed)
		{
			var exceptionLog = driver.CaptureDeviceCrashLog();
			_ = manager.CaptureFailScreenshot(driver.App, key, "CRASH");

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
			_ = manager.CaptureFailScreenshot(driver.App, key, sanitized);
		}
	}
}
