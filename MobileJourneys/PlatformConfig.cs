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

	/// <summary>The uiautomator2 setting that bounds how long a lookup waits for the UI to go quiet.</summary>
	internal const string WaitForIdleSetting = "waitForIdleTimeout";

	/// <summary>
	/// How long a lookup may wait for the device to become idle. The driver's own default is 10s,
	/// which a screen holding a progress spinner never reaches — the animation is exactly what "not
	/// idle" means — so every lookup taken while one is up costs the full 10s, and a step that means
	/// to inspect an overlay outlives the overlay. Journeys wait through their own polling
	/// expectations and screenshot-stability checks, so nothing here depends on the driver's.
	/// <para/>
	/// The trade-off is staleness: a lookup that returns mid-transition hands back an element the
	/// screen is about to replace, which a step then fails on. Raising this to soften that made
	/// things worse when measured, so it stays low and the staleness is worth handling where an
	/// element is used rather than by waiting longer everywhere.
	/// </summary>
	internal const int WaitForIdleTimeoutMs = 100;

	/// <summary>The mobile platform of this fixture.</summary>
	public abstract TestPlatform Platform { get; }

	/// <summary>The Appium automationName capability ("XCUITest" or "UiAutomator2").</summary>
	public abstract string AutomationName { get; }

	/// <summary>
	/// Allow small per-pixel color differences (e.g., JPEG decoding non-determinism).
	/// The number is the sum of the delta for each component, R, G, and B.
	/// </summary>
	public abstract int ColorTolerance { get; init; }

	/// <summary>
	/// Percentage of pixels permitted to exceed <see cref="ColorTolerance"/> before a step fails.
	/// A handful of pixels along a high-contrast edge can flip between near-black and near-white
	/// when the device decodes a photo at a different scale, which no colour threshold can separate
	/// from a real change; this budget absorbs them. It is a percentage rather than a count so that
	/// a regression of a given size in device-independent pixels stays equally detectable on every
	/// device — a real change scales with area, while this noise only scales with edge length.
	/// </summary>
	public abstract double MaxDiffPixelPercentage { get; init; }

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

	internal abstract void LaunchApp(AppiumDriver driver, IJourneyEnvironment environment, string backendUrlVariable);

	internal abstract void TerminateApp(AppiumDriver driver);

	internal abstract long QueryAppState(AppiumDriver driver);

	// --- Driver capabilities / Appium scripting ---

	internal abstract string DeviceIdCapabilityName { get; }

	internal abstract void OpenDeepLink(AppiumDriver driver, string url);

	internal abstract void PressHomeButton(AppiumDriver driver);

	// --- Host reachability ---

	/// <summary>
	/// Makes a port on this machine reachable from the device at the same port number. Does nothing
	/// where the device already shares the host's loopback; an Android emulator has one of its own,
	/// so the port has to be forwarded onto it.
	/// </summary>
	/// <param name="deviceId">The device to forward on.</param>
	/// <param name="port">The port number, the same on both sides.</param>
	public virtual void StartForwardingPort(string deviceId, int port) { }

	/// <summary>Undoes <see cref="StartForwardingPort"/>.</summary>
	/// <param name="deviceId">The device to stop forwarding on.</param>
	/// <param name="port">The port number that was forwarded.</param>
	public virtual void StopForwardingPort(string deviceId, int port) { }

	// --- Device readiness ---

	/// <summary>
	/// Blocks until this platform's devices are far enough through boot that a session can be started
	/// against them, or until <paramref name="timeout"/> elapses. Does nothing where a device reports
	/// itself attached only once it is genuinely usable.
	/// </summary>
	/// <param name="timeout">How long to wait before giving up and letting the caller try anyway.</param>
	internal virtual void WaitUntilDevicesAreReady(TimeSpan timeout) { }

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

	/// <summary>
	/// Where a notification banner's top edge sits on this device, in screenshot pixels.
	/// </summary>
	public abstract int NotificationBannerTop { get; init; }

	/// <summary>
	/// Where a notification banner's bottom edge sits on this device, in screenshot pixels. A step
	/// comparing only the banner masks everything below this line.
	/// </summary>
	public abstract int NotificationBannerBottom { get; init; }

	// --- System state setup ---

	internal abstract void SetSystemTheme(string deviceId, bool isLightTheme);

	internal abstract void SetSystemFontSize(string deviceId, SystemFontSize size);

	internal virtual void OnBeforeTests(TestDriver driver, string deviceId) { }

	// --- Dependency verification (called once at session start by DependencyChecker) ---

	internal abstract void VerifyDependencies();

	// --- Stale process cleanup (called once before the Appium server starts) ---

	/// <summary>
	/// Kills helper processes an earlier run left behind, so this one starts against a clean machine.
	/// Does nothing on a platform whose helpers do not outlive the run that started them.
	/// </summary>
	internal virtual void KillStaleHelperProcesses() { }

	internal static int FindFreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}
