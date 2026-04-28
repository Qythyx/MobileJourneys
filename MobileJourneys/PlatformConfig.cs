using System.Net;
using System.Net.Sockets;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;

namespace MobileJourneys;

/// <summary>
/// A simulator/emulator fixture the test suite runs against. Concrete subclasses are
/// <see cref="IosPlatformConfig"/> and <see cref="AndroidPlatformConfig"/>; consumers
/// build the four/N instances they need (typically one per platform × theme).
/// </summary>
/// <param name="PlatformVersion">The OS version, e.g., "26.2" for iOS, "15" for Android.</param>
/// <param name="DeviceName">The simulator/emulator name visible to xcrun/avd.</param>
/// <param name="IsLightTheme">When <c>true</c>, the system theme is forced to light before each journey.</param>
/// <param name="AppIdentifier">Bundle ID (iOS) or package name (Android), e.g., "jp.beercats.beerbox".</param>
/// <param name="AppBinaryPath">Absolute path to the .app bundle (iOS) or signed .apk (Android).</param>
public abstract record PlatformConfig(
	string PlatformVersion,
	string DeviceName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath
)
{
	internal const int SimulatorStartupTimeoutMs = 180_000;
	internal const int AvdLaunchTimeoutMs = 120_000;
	internal const int AppWaitDurationMs = 30_000;

	public abstract TestPlatform Platform { get; }
	public abstract string AutomationName { get; }

	/// <summary>
	/// Allow small per-pixel color differences (e.g., JPEG decoding non-determinism).
	/// The number is the sum of the delta for each component, R, G, and B.
	/// </summary>
	public abstract int ColorTolerance { get; }

	public string DisplayName => $"{Platform} · {PlatformVersion} · {DeviceName} · {(IsLightTheme ? "light" : "dark")}";

	public sealed override string ToString() => DisplayName;

	/// <summary>
	/// Builds an Appium driver and wraps it in a <see cref="TestDriver"/> tied to this fixture.
	/// </summary>
	/// <param name="deepLinkScheme">URL scheme used by driver helpers for in-app deep links (e.g., "beerbox").</param>
	public TestDriver GetTestDriver(string deepLinkScheme)
	{
		var options = new AppiumOptions
		{
			AutomationName = AutomationName,
			PlatformName = Platform.ToString(),
			PlatformVersion = PlatformVersion,
			DeviceName = DeviceName,
			App = ResolveAppBinaryPath(),
		};

		ConfigureAppiumOptions(options);
		options.AddAdditionalAppiumOption("newCommandTimeout", 120);

		var app = CreateDriver(options);
		return new TestDriver(app, this, deepLinkScheme);
	}

	private string ResolveAppBinaryPath() =>
		Path.Exists(AppBinaryPath)
			? AppBinaryPath
			: throw new FileNotFoundException(
				$"App binary not found at '{AppBinaryPath}'. "
					+ $"Build the app for the {Platform} target before running UI tests."
			);

	internal abstract void ConfigureAppiumOptions(AppiumOptions options);

	internal abstract AppiumDriver CreateDriver(AppiumOptions options);

	internal static int FindFreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}

/// <summary>
/// iOS simulator fixture (XCUITest).
/// </summary>
public sealed record IosPlatformConfig(
	string PlatformVersion,
	string DeviceName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath
) : PlatformConfig(PlatformVersion, DeviceName, IsLightTheme, AppIdentifier, AppBinaryPath)
{
	public override TestPlatform Platform => TestPlatform.iOS;
	public override string AutomationName => "XCUITest";
	public override int ColorTolerance => 3 * 2;

	internal override void ConfigureAppiumOptions(AppiumOptions options)
	{
		options.AddAdditionalAppiumOption("simulatorStartupTimeout", SimulatorStartupTimeoutMs);
		options.AddAdditionalAppiumOption("wdaLocalPort", FindFreePort());
		options.AddAdditionalAppiumOption("mjpegServerPort", FindFreePort());
	}

	internal override AppiumDriver CreateDriver(AppiumOptions options) => new IOSDriver(options);
}

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
public sealed record AndroidPlatformConfig(
	string PlatformVersion,
	string DeviceName,
	string AvdName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath,
	string? MainActivity
) : PlatformConfig(PlatformVersion, DeviceName, IsLightTheme, AppIdentifier, AppBinaryPath)
{
	public override TestPlatform Platform => TestPlatform.Android;
	public override string AutomationName => "UiAutomator2";
	public override int ColorTolerance => 3 * 10;

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
