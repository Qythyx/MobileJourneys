namespace MobileJourneys;

/// <summary>
/// Verifies external dependencies (Appium, Xcode tools, Android SDK platform-tools)
/// required to drive the configured fixtures. <see cref="Framework.SuiteRunner"/>
/// calls <see cref="Verify"/> at the start of every run; missing dependencies fail
/// early with an actionable install hint instead of producing cryptic Appium errors
/// later in the session.
/// </summary>
public static class DependencyChecker
{
	private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Probes every external binary the configured fixtures need. Throws
	/// <see cref="InvalidOperationException"/> with an install hint on the first
	/// missing or non-functional dependency.
	/// </summary>
	/// <param name="config">Framework configuration; only platforms present in
	/// <see cref="FrameworkConfig.PlatformConfigs"/> are checked.</param>
	public static void Verify(FrameworkConfig config)
	{
		// Appium is always required — the framework starts a local Appium server
		// for every test session regardless of which platforms are configured.
		RequireBinary(
			"appium",
			"--version",
			"Install Appium 2.x: `npm install -g appium` (and ensure node/npm are on PATH)."
		);

		foreach (var platformConfig in config.PlatformConfigs.GroupBy(p => p.Platform).Select(g => g.First()))
		{
			platformConfig.VerifyDependencies();
		}
	}

	internal static void RequireBinary(string binary, string args, string installHint)
	{
		var argList = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var result = ProcessRunner.RunWithResult(binary, argList, (int)ProbeTimeout.TotalSeconds);
		if (result is null)
		{
			throw new InvalidOperationException($"Failed to start '{binary}'.\n  {installHint}");
		}

		switch (result.Status)
		{
			case ProcessRunner.ShellResultStatus.FailedToStart:
				throw new InvalidOperationException(
					$"Required dependency '{binary}' not found on PATH.\n  {installHint}"
				);
			case ProcessRunner.ShellResultStatus.TimedOut:
				throw new InvalidOperationException(
					$"`{binary} {args}` did not exit within {ProbeTimeout.TotalSeconds:F0}s.\n  {installHint}"
				);
			case ProcessRunner.ShellResultStatus.Completed when result.ExitCode != 0:
				var stderr = result.Error.Trim();
				throw new InvalidOperationException(
					$"`{binary} {args}` exited with code {result.ExitCode}.\n"
						+ (string.IsNullOrEmpty(stderr) ? string.Empty : $"  stderr: {stderr}\n")
						+ $"  {installHint}"
				);
		}
	}
}
