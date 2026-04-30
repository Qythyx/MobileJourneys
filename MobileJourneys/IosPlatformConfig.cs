using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.iOS;

namespace MobileJourneys;

/// <summary>
/// iOS simulator fixture (XCUITest).
/// </summary>
public sealed record IosPlatformConfig(
	string PlatformVersion,
	string DeviceName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath,
	int MaxScreenshotHeight
) : PlatformConfig(PlatformVersion, DeviceName, IsLightTheme, AppIdentifier, AppBinaryPath, MaxScreenshotHeight)
{
	/// <inheritdoc/>
	public override TestPlatform Platform => TestPlatform.iOS;

	/// <inheritdoc/>
	public override string AutomationName => "XCUITest";

	/// <inheritdoc/>
	public override int ColorTolerance => 3 * 2;

	internal override void ConfigureAppiumOptions(AppiumOptions options)
	{
		options.AddAdditionalAppiumOption("simulatorStartupTimeout", SimulatorStartupTimeoutMs);
		options.AddAdditionalAppiumOption("wdaLocalPort", FindFreePort());
		options.AddAdditionalAppiumOption("mjpegServerPort", FindFreePort());
	}

	internal override AppiumDriver CreateDriver(AppiumOptions options) => new IOSDriver(options);

	internal override void LaunchApp(AppiumDriver driver, IJourneyEnvironment environment) =>
		_ = driver.ExecuteScript(
			"mobile: launchApp",
			new Dictionary<string, object> { ["bundleId"] = AppIdentifier, ["environment"] = environment.GetEnvVars() }
		);

	internal override void TerminateApp(AppiumDriver driver) =>
		_ = driver.ExecuteScript(
			"mobile: terminateApp",
			new Dictionary<string, object> { ["bundleId"] = AppIdentifier }
		);

	internal override long QueryAppState(AppiumDriver driver)
	{
		var result = driver.ExecuteScript(
			"mobile: queryAppState",
			new Dictionary<string, object> { ["bundleId"] = AppIdentifier }
		);
		return result is long state ? state : 0;
	}

	internal override string DeviceIdCapabilityName => "udid";

	internal override void OpenDeepLink(AppiumDriver driver, string url) =>
		// iOS has no mobile: deepLink command. Navigate to the URL directly — XCUITest
		// delivers custom URL schemes to the foreground app without a system confirmation dialog.
		driver.Navigate().GoToUrl(url);

	internal override void PressHomeButton(AppiumDriver driver) =>
		_ = driver.ExecuteScript("mobile: pressButton", new Dictionary<string, object> { ["name"] = "home" });

	internal override void DismissKeyboard(AppiumDriver driver)
	{
		// HideKeyboard() is unreliable on iOS 26+ — it may silently fail
		// or throw. Try the MAUI Done button (input accessory toolbar added by
		// MauiDoneAccessoryView for Editor/Picker controls) first, then fall
		// back to the keyboard Return key for Entry controls.
		try
		{
			driver.FindElement(By.XPath("//XCUIElementTypeToolbar//XCUIElementTypeButton")).Click();
			return;
		}
		catch
		{
			/* no toolbar button — not an Editor/Picker, or keyboard not visible */
		}

		try
		{
			driver.FindElement(By.XPath("//XCUIElementTypeKeyboard//XCUIElementTypeButton[@name='Return']")).Click();
		}
		catch
		{
			/* keyboard may not be visible */
		}
	}

	internal override void DismissDefaultAlert(IAlert alert) =>
		// On iOS XCUITest with MAUI, Accept() maps to the cancel button
		// of a two-button DisplayAlert (the one that does nothing).
		alert.Accept();

	internal override By GetAlertButtonLocator(string buttonLabel) => By.Name(buttonLabel);

	internal override string? ReadCrashLog(string deviceId)
	{
		var containerPath = ProcessRunner
			.RunWithResult("xcrun", ["simctl", "get_app_container", deviceId, AppIdentifier, "data"])
			?.Output?.Trim();

		if (string.IsNullOrWhiteSpace(containerPath))
		{
			return null;
		}

		// The app writes crash logs to Path.GetTempPath() which maps to tmp/ on iOS.
		var crashLogPath = Path.Combine(containerPath, "tmp", "crash.log");
		if (!File.Exists(crashLogPath))
		{
			return null;
		}

		var content = File.ReadAllText(crashLogPath);
		File.Delete(crashLogPath);
		return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
	}

	internal override void CaptureDeviceScreenshot(string deviceId, string outPath) =>
		ProcessRunner.RunWithResult("xcrun", ["simctl", "io", deviceId, "screenshot", outPath]);

	internal override int GetStatusBarHeight(AppiumDriver driver) =>
		driver.GetDict("mobile: deviceScreenInfo").GetDict("statusBarSize").GetInt("height");

	// Can't probe the home indicator height from Appium — hardcode the standard 34pt.
	internal override int GetHomeIndicatorHeight(AppiumDriver driver) => 34;

	internal override int NotificationBannerMaskHeight => 110;

	internal override int NotificationBannerTapYOffset => 100;

	internal override void SetSystemTheme(string deviceId, bool isLightTheme) =>
		ProcessRunner.Run("xcrun", ["simctl", "ui", deviceId, "appearance", isLightTheme ? "light" : "dark"]);

	internal override void SetSystemFontSize(string deviceId, SystemFontSize size) =>
		ProcessRunner.Run("xcrun", ["simctl", "ui", deviceId, "content_size", ToIosContentSize(size)]);

	internal override void VerifyDependencies() =>
		DependencyChecker.RequireBinary(
			"xcrun",
			"--version",
			"Install Xcode command-line tools: `xcode-select --install`."
		);

	internal override void OnBeforeTests(TestDriver driver, string deviceId)
	{
		// Enables hardware keyboard on an iOS simulator so the software keyboard doesn't appear.
		// The Appium connectHardwareKeyboard capability only works when Appium launches the
		// simulator; for pre-booted simulators the preference must be set directly.
		ProcessRunner.Run(
			"xcrun",
			[
				"simctl",
				"spawn",
				deviceId,
				"defaults",
				"write",
				"com.apple.Preferences",
				"ConnectHardwareKeyboard",
				"-bool",
				"true",
			]
		);
		driver.DismissAlertIfPresent(TimeSpan.FromSeconds(5));
	}

	private static string ToIosContentSize(SystemFontSize size) =>
		size switch
		{
			SystemFontSize.ExtraSmall => "extra-small",
			SystemFontSize.Small => "small",
			SystemFontSize.Medium => "medium",
			SystemFontSize.Large => "large",
			SystemFontSize.ExtraLarge => "extra-large",
			SystemFontSize.ExtraExtraLarge => "extra-extra-large",
			SystemFontSize.ExtraExtraExtraLarge => "extra-extra-extra-large",
			SystemFontSize.AccessibilityMedium => "accessibility-medium",
			SystemFontSize.AccessibilityLarge => "accessibility-large",
			SystemFontSize.AccessibilityExtraLarge => "accessibility-extra-large",
			SystemFontSize.AccessibilityExtraExtraLarge => "accessibility-extra-extra-large",
			SystemFontSize.AccessibilityExtraExtraExtraLarge => "accessibility-extra-extra-extra-large",
			_ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
		};
}
