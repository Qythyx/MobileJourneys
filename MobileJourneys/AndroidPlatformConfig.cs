using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace MobileJourneys;

/// <summary>
/// Android emulator fixture (UiAutomator2).
/// </summary>
/// <param name="PlatformVersion">The Android version, e.g., "15".</param>
/// <param name="DeviceName">The emulator's display name (purely cosmetic for IDE/test reporting).</param>
/// <param name="AvdName">The Android Virtual Device name (e.g., "Pixel_8_API35").</param>
/// <param name="IsLightTheme">When <c>true</c>, the system theme is forced to light before each journey.</param>
/// <param name="AppIdentifier">Package name (e.g., "jp.beercats.beerbox").</param>
/// <param name="AppBinaryPath">Absolute path to the signed .apk.</param>
/// <param name="MainActivity">Optional. Defaults to <c>$"{AppIdentifier}.MainActivity"</c>.</param>
/// <param name="ColorTolerance">Permitted per-pixel color delta (sum of R, G, B deltas) when comparing against baselines.</param>
public sealed record AndroidPlatformConfig(
	string PlatformVersion,
	string DeviceName,
	string AvdName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath,
	string? MainActivity,
	int ColorTolerance = 3 * 10
) : PlatformConfig(PlatformVersion, DeviceName, IsLightTheme, AppIdentifier, AppBinaryPath)
{
	/// <inheritdoc/>
	public override TestPlatform Platform => TestPlatform.Android;

	/// <inheritdoc/>
	public override string AutomationName => "UiAutomator2";

	/// <summary>The activity to launch on app start, falling back to <c>$"{AppIdentifier}.MainActivity"</c>.</summary>
	public string ResolvedMainActivity => MainActivity ?? $"{AppIdentifier}.MainActivity";

	private static string AdbPath =>
		Path.Combine(
			Environment.GetEnvironmentVariable("ANDROID_HOME")
				?? throw new InvalidOperationException(MissingAndroidHomeMessage),
			"platform-tools",
			"adb"
		);

	private const string MissingAndroidHomeMessage =
		"ANDROID_HOME environment variable is not set. Install the Android SDK "
		+ "(e.g., via Android Studio) and export ANDROID_HOME to the SDK directory "
		+ "(typically `$HOME/Library/Android/sdk` on macOS).";

	/// <summary>
	/// Selects the Nth item in the MAUI Picker with the given AutomationId on Android.
	/// </summary>
	/// <param name="driver">The test driver bound to this Android fixture.</param>
	/// <param name="automationId">The AutomationId of the Picker element.</param>
	/// <param name="itemIndex">Zero-based index of the item to select.</param>
	public static void SelectPickerItem(TestDriver driver, string automationId, int itemIndex)
	{
		driver.FindElement(automationId, TimeSpan.FromSeconds(5)).Click();
		TestDriver.WaitForAppToSettle(500);

		// Android MAUI Picker: opens an AlertDialog with CheckedTextView radio button items.
		// Wait for the dialog to appear before looking for items.
		var wait = new WebDriverWait(driver.App, TimeSpan.FromSeconds(5));
		wait.IgnoreExceptionTypes(typeof(NoSuchElementException));
		_ = wait.Until(d => d.FindElements(MobileBy.ClassName("android.widget.CheckedTextView")).Count > 0);

		var items = driver.App.FindElements(MobileBy.ClassName("android.widget.CheckedTextView"));
		if (itemIndex >= items.Count)
		{
			throw new ArgumentOutOfRangeException(
				nameof(itemIndex),
				itemIndex,
				$"Picker item index out of range (found {items.Count} items)"
			);
		}

		items[itemIndex].Click();
	}

	internal override void ConfigureAppiumOptions(AppiumOptions options)
	{
		options.AddAdditionalAppiumOption("appPackage", AppIdentifier);
		options.AddAdditionalAppiumOption("appActivity", ResolvedMainActivity);
		options.AddAdditionalAppiumOption("appWaitDuration", AppWaitDurationMs);
		options.AddAdditionalAppiumOption("autoGrantPermissions", true);
		options.AddAdditionalAppiumOption("enforceAppInstall", true);
		options.AddAdditionalAppiumOption("avd", AvdName);
		options.AddAdditionalAppiumOption("avdLaunchTimeout", AvdLaunchTimeoutMs);
	}

	internal override AppiumDriver CreateDriver(AppiumOptions options) => new AndroidDriver(options);

	internal override void LaunchApp(AppiumDriver driver, IJourneyEnvironment environment)
	{
		var amArgs = new List<string>(
			new string[] { "start-activity", "-S", "-n", $"{AppIdentifier}/{ResolvedMainActivity}" }.Concat(
				environment.GetEnvVars().SelectMany(kv => new string[] { "--es", kv.Key, $"'{kv.Value}'" })
			)
		);

		_ = driver.ExecuteScript(
			"mobile: shell",
			new Dictionary<string, object> { ["command"] = "am", ["args"] = amArgs }
		);
	}

	internal override void TerminateApp(AppiumDriver driver) =>
		_ = driver.ExecuteScript("mobile: terminateApp", new Dictionary<string, object> { ["appId"] = AppIdentifier });

	internal override long QueryAppState(AppiumDriver driver)
	{
		var result = driver.ExecuteScript(
			"mobile: queryAppState",
			new Dictionary<string, object> { ["appId"] = AppIdentifier }
		);
		return result is long state ? state : 0;
	}

	internal override string DeviceIdCapabilityName => "deviceUDID";

	internal override void OpenDeepLink(AppiumDriver driver, string url) =>
		_ = driver.ExecuteScript(
			"mobile: deepLink",
			new Dictionary<string, object> { ["url"] = url, ["package"] = AppIdentifier }
		);

	internal override void PressHomeButton(AppiumDriver driver) =>
		_ = driver.ExecuteScript("mobile: pressKey", new Dictionary<string, object> { ["keycode"] = 3 });

	internal override void DismissDefaultAlert(IAlert alert) => alert.Dismiss();

	internal override By GetAlertButtonLocator(string buttonLabel)
	{
		// Android AlertDialog buttons don't have AccessibilityId. Find by text content.
		// MaterialAlertDialog renders button text in uppercase via textAllCaps, so the
		// @text attribute contains the uppercased form. Use translate() for a
		// case-insensitive match so journey definitions can use the natural-case label.
		var lower = buttonLabel.ToLowerInvariant();
		return By.XPath(
			$"//android.widget.Button[translate(@text,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')={XPathLiteral(lower)}]"
		);
	}

	// XPath 1.0 has no escape mechanism inside string literals, so values containing both
	// kinds of quotes (e.g., "Don't") must be assembled via concat(). For values with only
	// one kind, the other kind of quote suffices.
	private static string XPathLiteral(string value)
	{
		if (!value.Contains('\''))
		{
			return $"'{value}'";
		}
		if (!value.Contains('"'))
		{
			return $"\"{value}\"";
		}
		var parts = value.Split('\'');
		return "concat(" + string.Join(", \"'\", ", parts.Select(p => $"'{p}'")) + ")";
	}

	internal override string? ReadCrashLog(string deviceId)
	{
		// The app's private data dir (/data/data/<package>/) is only accessible via run-as.
		// Must specify -s <device> because multiple emulators may be running.
		const string CrashLog = "crash.log";

		// When the app crashed, it may still be writing the log file. Retry a few times.
		const int maxAttempts = 3;
		for (var attempt = 0; attempt < maxAttempts; attempt++)
		{
			if (attempt > 0)
			{
				Thread.Sleep(500);
			}

			var result = RunAdb(deviceId, "shell", "run-as", AppIdentifier, "cat", $"cache/{CrashLog}");
			if (result is null)
			{
				continue;
			}

			if (
				!string.IsNullOrWhiteSpace(result.Output)
				&& !result.Output.Contains("No such file", StringComparison.Ordinal)
			)
			{
				_ = RunAdb(deviceId, "shell", "run-as", AppIdentifier, "rm", $"cache/{CrashLog}");
				return result.Output.Trim();
			}
		}

		// crash.log not available — fall back to logcat for the crash stack trace.
		var logcat = CaptureLogcat(deviceId);
		return logcat is not null ? $"[from logcat — crash.log was not available]\n{logcat}" : null;
	}

	internal override void ClearAppLogs(string deviceId) => _ = RunAdb(deviceId, "logcat", "-c");

	internal override void CaptureDeviceScreenshot(string deviceId, string outPath)
	{
		const string DeviceTmp = "/data/local/tmp/test_screenshot.png";
		_ = RunAdb(deviceId, "shell", "screencap", "-p", DeviceTmp);
		_ = RunAdb(deviceId, "pull", DeviceTmp, outPath);
		_ = RunAdb(deviceId, "shell", "rm", DeviceTmp);
	}

	internal override int GetStatusBarHeight(AppiumDriver driver) =>
		driver.GetDict("mobile: getSystemBars").GetDict("statusBar").GetInt("height");

	internal override int GetHomeIndicatorHeight(AppiumDriver driver) =>
		driver.GetDict("mobile: getSystemBars").GetDict("navigationBar").GetInt("height");

	internal override int NotificationBannerMaskHeight => 350;

	internal override int NotificationBannerTapYOffset => 200;

	internal override void SetSystemTheme(string deviceId, bool isLightTheme) =>
		ProcessRunner.Run(AdbPath, ["-s", deviceId, "shell", "cmd", "uimode", "night", isLightTheme ? "no" : "yes"]);

	internal override void SetSystemFontSize(string deviceId, SystemFontSize size)
	{
		var scale = ToAndroidFontScale(size).ToString(System.Globalization.CultureInfo.InvariantCulture);
		ProcessRunner.Run(AdbPath, ["-s", deviceId, "shell", "settings", "put", "system", "font_scale", scale]);
	}

	internal override void OnBeforeTests(TestDriver driver, string deviceId)
	{
		// On a fresh emulator, Chrome answers its first VIEW intent with the first-run
		// experience instead of loading the requested URL, which strands any journey that
		// leaves the app through the system browser. The command-line file (honored on
		// userdebug system images) skips the first run, and pre-granting the notification
		// permission suppresses the one-time notifications promo dialog that would otherwise
		// cover the page. Both are idempotent and persist in the AVD.
		_ = RunAdb(
			deviceId,
			"shell",
			"echo 'chrome --no-first-run --disable-fre --no-default-browser-check' > /data/local/tmp/chrome-command-line"
		);
		_ = RunAdb(deviceId, "shell", "pm", "grant", "com.android.chrome", "android.permission.POST_NOTIFICATIONS");
	}

	internal override void VerifyDependencies()
	{
		var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
		if (string.IsNullOrEmpty(androidHome))
		{
			throw new InvalidOperationException(MissingAndroidHomeMessage);
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

		DependencyChecker.RequireBinary(
			adbPath,
			"--version",
			$"adb at '{adbPath}' is present but failed to run. Reinstall Android platform-tools."
		);
	}

	private static string? CaptureLogcat(string deviceId)
	{
		// AndroidRuntime:E  — fatal Java/Kotlin exception stack traces
		// MonoUnhandled:E   — .NET unhandled exception output
		// DOTNET:E          — .NET runtime error output
		// monodroid:F       — native startup failures (e.g. Fast Deployment missing assemblies)
		// DEBUG:F           — native crash abort messages with stack traces
		// Logcat is cleared before each app launch, so -t is not needed — all entries
		// in the buffer are from the current launch attempt.
		var result = RunAdb(
			deviceId,
			"logcat",
			"-d",
			"AndroidRuntime:E",
			"MonoUnhandled:E",
			"DOTNET:E",
			"monodroid:F",
			"DEBUG:F",
			"*:S"
		);
		return string.IsNullOrWhiteSpace(result?.Output) ? null : result.Output.Trim();
	}

	private static ProcessRunner.ShellResult? RunAdb(string deviceId, params string[] arguments)
	{
		var args = new List<string>(arguments.Length + 2) { "-s", deviceId };
		args.AddRange(arguments);
		return ProcessRunner.RunWithResult(AdbPath, args);
	}

	internal static double ToAndroidFontScale(SystemFontSize size) =>
		size switch
		{
			SystemFontSize.ExtraSmall => 0.82,
			SystemFontSize.Small => 0.88,
			SystemFontSize.Medium => 0.94,
			SystemFontSize.Large => 1.0,
			SystemFontSize.ExtraLarge => 1.15,
			SystemFontSize.ExtraExtraLarge => 1.30,
			SystemFontSize.ExtraExtraExtraLarge => 1.45,
			SystemFontSize.AccessibilityMedium => 1.6,
			SystemFontSize.AccessibilityLarge => 1.8,
			SystemFontSize.AccessibilityExtraLarge => 2.0,
			SystemFontSize.AccessibilityExtraExtraLarge => 2.25,
			SystemFontSize.AccessibilityExtraExtraExtraLarge => 2.5,
			_ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
		};
}
