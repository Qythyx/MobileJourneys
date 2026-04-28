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
}
