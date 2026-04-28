using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

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
/// <param name="MaxScreenshotHeight">See <see cref="PlatformConfig.MaxScreenshotHeight"/>.</param>
public sealed record AndroidPlatformConfig(
	string PlatformVersion,
	string DeviceName,
	string AvdName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath,
	string? MainActivity,
	int MaxScreenshotHeight
) : PlatformConfig(PlatformVersion, DeviceName, IsLightTheme, AppIdentifier, AppBinaryPath, MaxScreenshotHeight)
{
	/// <inheritdoc/>
	public override TestPlatform Platform => TestPlatform.Android;

	/// <inheritdoc/>
	public override string AutomationName => "UiAutomator2";

	/// <inheritdoc/>
	public override int ColorTolerance => 3 * 10;

	/// <summary>The activity to launch on app start, falling back to <c>$"{AppIdentifier}.MainActivity"</c>.</summary>
	public string ResolvedMainActivity => MainActivity ?? $"{AppIdentifier}.MainActivity";

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
}
