using System.Diagnostics;

namespace MobileJourneys.Framework;

internal static class JourneyRunner
{
	public static async Task<JourneyResult> RunAsync(TestDriver driver, TestCase testCase, MtpReporter reporter)
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
			(1, journey.InitialName, () => ProcessExpectations(driver, journey.InitialExpect), [], false, null),
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

		foreach (var (number, name, execute, maskElements, prefetchMasks, journeyStep) in steps)
		{
			await reporter.StepStartedAsync(testCase, number, totalSteps, name);
			var numberedStepName = $"{number:D2} {name}";
			try
			{
				FailedTestScanner.CleanupStepResults(driver.Config, journey.Name, numberedStepName);
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
						+ $"  {(stepResult.ScreenshotDir is null ? "" : new Uri(stepResult.ScreenshotDir).AbsoluteUri)}";
					stopwatch.Stop();
					return new JourneyResult(
						testCase,
						false,
						stopwatch.Elapsed,
						message,
						new JourneyFailureException(message, journey, journeyStep, number, totalSteps, name)
					);
				}
			}
			catch (Exception ex)
			{
				HandleStepFailure(driver, ex, numberedStepName, journey.Name);
				var message = $"step {number}/{totalSteps}: {name} — {ex.Message}";
				stopwatch.Stop();
				return new JourneyResult(
					testCase,
					false,
					stopwatch.Elapsed,
					message,
					new JourneyFailureException(message, journey, journeyStep, number, totalSteps, name, ex)
				);
			}
		}

		FailedTestScanner.CleanupResults(driver.Config, journey.Name);
		stopwatch.Stop();
		return new JourneyResult(testCase, true, stopwatch.Elapsed, string.Empty, null);
	}

	private static void ProcessExpectations(TestDriver driver, Expectation[] expectations)
	{
		foreach (var expectation in expectations)
		{
			expectation.Verify(driver);
		}
	}

	private static void HandleStepFailure(TestDriver driver, Exception ex, string filePrefix, string journeyName)
	{
		var appCrashed = driver.IsAppCrashed();
		if (appCrashed)
		{
			var exceptionLog = driver.CaptureDeviceCrashLog();
			_ = ScreenshotHelper.CaptureScreenshot(driver.App, driver.Config, journeyName, $"{filePrefix}_FAIL_CRASH");

			var logDir = ScreenshotHelper.GetScreenshotsDir(driver.Config, journeyName);
			_ = Directory.CreateDirectory(logDir);

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
			File.WriteAllText(Path.Combine(logDir, $"{filePrefix}.CRASH.txt"), logContent);
		}
		else
		{
			var sanitized = new string([.. ex.Message.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);
			sanitized = sanitized.Length > 80 ? sanitized[..80] : sanitized;
			_ = ScreenshotHelper.CaptureScreenshot(
				driver.App,
				driver.Config,
				journeyName,
				$"{filePrefix}_FAIL_{sanitized}"
			);
		}
	}
}
