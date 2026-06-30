using System.Net;
using System.Net.Sockets;
using OpenQA.Selenium;
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

	/// <summary>The mobile platform of this fixture.</summary>
	public abstract TestPlatform Platform { get; }

	/// <summary>The Appium automationName capability ("XCUITest" or "UiAutomator2").</summary>
	public abstract string AutomationName { get; }

	/// <summary>
	/// Allow small per-pixel color differences (e.g., JPEG decoding non-determinism).
	/// The number is the sum of the delta for each component, R, G, and B.
	/// </summary>
	public abstract int ColorTolerance { get; init; }

	/// <summary>Human-readable identifier used as the screenshot subdirectory name.</summary>
	public string DisplayName => $"{Platform} · {PlatformVersion} · {DeviceName} · {(IsLightTheme ? "light" : "dark")}";

	/// <inheritdoc/>
	public sealed override string ToString() => DisplayName;

	/// <summary>
	/// Builds and starts an Appium driver bound to this fixture. The framework wraps the
	/// returned driver in a <see cref="TestDriver"/>; consumers do not call this directly.
	/// </summary>
	internal AppiumDriver CreateAppiumDriver()
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
		return CreateDriver(options);
	}

	internal string ResolveAppBinaryPath() =>
		Path.Exists(AppBinaryPath)
			? AppBinaryPath
			: throw new FileNotFoundException(
				$"App binary not found at '{AppBinaryPath}'. "
					+ $"Build the app for the {Platform} target before running UI tests."
			);

	internal abstract void ConfigureAppiumOptions(AppiumOptions options);

	internal abstract AppiumDriver CreateDriver(AppiumOptions options);

	// --- App lifecycle ---

	internal abstract void LaunchApp(AppiumDriver driver, IJourneyEnvironment environment);

	internal abstract void TerminateApp(AppiumDriver driver);

	internal abstract long QueryAppState(AppiumDriver driver);

	// --- Driver capabilities / Appium scripting ---

	internal abstract string DeviceIdCapabilityName { get; }

	internal abstract void OpenDeepLink(AppiumDriver driver, string url);

	internal abstract void PressHomeButton(AppiumDriver driver);

	// --- Keyboard / alerts ---

	internal virtual void DismissKeyboard(AppiumDriver driver) => driver.HideKeyboard();

	internal abstract void DismissDefaultAlert(IAlert alert);

	internal abstract By GetAlertButtonLocator(string buttonLabel);

	// --- Crash logs / device logs ---

	internal abstract string? ReadCrashLog(string deviceId);

	internal virtual void ClearAppLogs(string deviceId) { }

	// --- Screenshots / system UI ---

	internal abstract void CaptureDeviceScreenshot(string deviceId, string outPath);

	internal abstract int GetStatusBarHeight(AppiumDriver driver);

	internal abstract int GetHomeIndicatorHeight(AppiumDriver driver);

	internal abstract int NotificationBannerMaskHeight { get; }

	internal abstract int NotificationBannerTapYOffset { get; }

	// --- System state setup ---

	internal abstract void SetSystemTheme(string deviceId, bool isLightTheme);

	internal abstract void SetSystemFontSize(string deviceId, SystemFontSize size);

	internal virtual void OnBeforeTests(TestDriver driver, string deviceId) { }

	// --- Dependency verification (called once at session start by DependencyChecker) ---

	internal abstract void VerifyDependencies();

	internal static int FindFreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}
