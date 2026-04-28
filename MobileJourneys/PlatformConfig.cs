using System.Net;
using System.Net.Sockets;
using OpenQA.Selenium.Appium;

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
/// <param name="MaxScreenshotHeight">
/// Maximum height (in pixels) screenshots are scaled down to before saving as a baseline
/// or comparing against one. Larger devices' raw screenshots are downscaled proportionally.
/// Choose to trade off baseline file size and visual fidelity (e.g., 2000).
/// </param>
public abstract record PlatformConfig(
	string PlatformVersion,
	string DeviceName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath,
	int MaxScreenshotHeight
)
{
	internal const int SimulatorStartupTimeoutMs = 180_000;
	internal const int AvdLaunchTimeoutMs = 120_000;
	internal const int AppWaitDurationMs = 30_000;

	/// <summary>The mobile platform of this fixture.</summary>
	public abstract TestPlatform Platform { get; }

	/// <summary>The Appium automationName capability ("XCUITest" or "UiAutomator2").</summary>
	public abstract string AutomationName { get; }

	/// <summary>
	/// Allow small per-pixel color differences (e.g., JPEG decoding non-determinism).
	/// The number is the sum of the delta for each component, R, G, and B.
	/// </summary>
	public abstract int ColorTolerance { get; }

	/// <summary>Human-readable identifier used as the screenshot subdirectory name.</summary>
	public string DisplayName => $"{Platform} · {PlatformVersion} · {DeviceName} · {(IsLightTheme ? "light" : "dark")}";

	/// <inheritdoc/>
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
