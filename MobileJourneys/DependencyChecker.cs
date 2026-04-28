using System.ComponentModel;
using System.Diagnostics;

namespace MobileJourneys;

/// <summary>
/// Verifies external dependencies (Appium, Xcode tools, Android SDK platform-tools)
/// required to drive the configured fixtures. <see cref="Framework.TestFramework"/>
/// calls <see cref="Verify"/> at the start of every test run; missing dependencies fail
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

		var platforms = new HashSet<TestPlatform>(config.PlatformConfigs.Select(p => p.Platform));

		if (platforms.Contains(TestPlatform.iOS))
		{
			RequireBinary("xcrun", "--version", "Install Xcode command-line tools: `xcode-select --install`.");
		}

		if (platforms.Contains(TestPlatform.Android))
		{
			VerifyAndroidPlatformTools();
		}
	}

	private static void VerifyAndroidPlatformTools()
	{
		var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
		if (string.IsNullOrEmpty(androidHome))
		{
			throw new InvalidOperationException(
				"ANDROID_HOME environment variable is not set. Install the Android SDK "
					+ "(e.g., via Android Studio) and export ANDROID_HOME to the SDK directory "
					+ "(typically `$HOME/Library/Android/sdk` on macOS)."
			);
		}

		var adbPath = Path.Combine(androidHome, "platform-tools", "adb");
		if (!File.Exists(adbPath))
		{
			throw new InvalidOperationException(
				$"adb not found at '{adbPath}'. Install platform-tools via Android Studio's "
					+ "SDK Manager, or download standalone from "
					+ "https://developer.android.com/tools/releases/platform-tools."
			);
		}

		RequireBinary(
			adbPath,
			"--version",
			$"adb at '{adbPath}' is present but failed to run. Reinstall Android platform-tools."
		);
	}

	private static void RequireBinary(string binary, string args, string installHint)
	{
		Process? process = null;
		try
		{
			process = Process.Start(
				new ProcessStartInfo
				{
					FileName = binary,
					Arguments = args,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
				}
			);
		}
		catch (Win32Exception ex)
		{
			throw new InvalidOperationException(
				$"Required dependency '{binary}' not found on PATH.\n  {installHint}",
				ex
			);
		}

		if (process is null)
		{
			throw new InvalidOperationException($"Failed to start '{binary}'.\n  {installHint}");
		}

		using (process)
		{
			if (!process.WaitForExit(ProbeTimeout))
			{
				process.Kill(entireProcessTree: true);
				throw new InvalidOperationException(
					$"`{binary} {args}` did not exit within {ProbeTimeout.TotalSeconds:F0}s.\n" + $"  {installHint}"
				);
			}

			if (process.ExitCode != 0)
			{
				var stderr = process.StandardError.ReadToEnd().Trim();
				throw new InvalidOperationException(
					$"`{binary} {args}` exited with code {process.ExitCode}.\n"
						+ (string.IsNullOrEmpty(stderr) ? string.Empty : $"  stderr: {stderr}\n")
						+ $"  {installHint}"
				);
			}
		}
	}
}
