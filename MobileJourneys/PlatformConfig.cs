using System.Net;
using System.Net.Sockets;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
	/// How many devices this fixture runs on at once. Each is a separate simulator or emulator
	/// instance with its own Appium session, and the fixture's journeys are shared between them.
	/// </summary>
	public int Instances
	{
		get;
		init
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(Instances));
			field = value;
		}
	} = 1;

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
	/// Builds and starts an Appium driver bound to one of this fixture's devices. The framework
	/// wraps the returned driver in a <see cref="TestDriver"/>; consumers do not call this directly.
	/// </summary>
	/// <param name="deviceId">The simulator UDID or emulator serial to open the session on.</param>
	internal AppiumDriver CreateAppiumDriver(string deviceId)
	{
		var options = new AppiumOptions
		{
			AutomationName = AutomationName,
			PlatformName = Platform.ToString(),
			PlatformVersion = PlatformVersion,
			DeviceName = DeviceName,
			App = ResolveAppBinaryPath(),
		};

		options.AddAdditionalAppiumOption("udid", deviceId);
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
	/// Brings this fixture's <see cref="Instances"/> devices up, creating any that do not exist yet,
	/// and names them. The automation server is then handed a device that is already up, so it
	/// never starts one of its own.
	/// </summary>
	/// <param name="timeout">How long to wait for a started device to become addressable.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	/// <returns>One device id per instance, in worker order.</returns>
	/// <exception cref="InvalidOperationException">A device could not be found, created, or started.</exception>
	/// <exception cref="OperationCanceledException">The run was interrupted.</exception>
	internal abstract IReadOnlyList<string> StartDevices(TimeSpan timeout, CancellationToken cancellationToken);

	/// <summary>
	/// Blocks until a device is far enough through boot that a session can be started against it,
	/// or until <paramref name="timeout"/> elapses. Returns at once where a device reports itself
	/// attached only once it is genuinely usable.
	/// </summary>
	/// <param name="deviceId">The device to wait for.</param>
	/// <param name="timeout">How long to wait for a device that is still booting.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	/// <returns>How far the device got by the time the wait ended.</returns>
	/// <exception cref="OperationCanceledException">The run was interrupted.</exception>
	internal virtual DeviceReadiness WaitUntilDeviceIsReady(
		string deviceId,
		TimeSpan timeout,
		CancellationToken cancellationToken
	) => DeviceReadiness.Ready;

	/// <summary>What <see cref="WaitUntilDeviceIsReady"/> found the device doing.</summary>
	internal enum DeviceReadiness
	{
		/// <summary>A session can be started against it.</summary>
		Ready = 0,

		/// <summary>It was still booting when the wait ran out.</summary>
		Booting = 1,

		/// <summary>It has booted, but no longer answers, and only <see cref="RestartDevice"/> brings it back.</summary>
		Unresponsive = 2,
	}

	/// <summary>
	/// Replaces a running device with a fresh boot of the same one, under the same device id. The
	/// device may be mid-boot on return; <see cref="WaitUntilDeviceIsReady"/> waits it out. Once the
	/// old device is gone the new one is always started, so a restart never leaves a device dead.
	/// </summary>
	/// <param name="deviceId">The device to restart.</param>
	/// <param name="timeout">How long to wait for the old device to go.</param>
	/// <returns>
	/// Whether a fresh boot was started. <c>false</c> where the platform cannot restart a device, or
	/// could not tell which running device to stop.
	/// </returns>
	internal virtual bool RestartDevice(string deviceId, TimeSpan timeout) => false;

	/// <summary>
	/// Whether <see cref="RestartDevice"/> does anything on this platform. Announcing a restart that
	/// cannot happen would leave a worker reading as busy with something it is not doing.
	/// </summary>
	internal virtual bool CanRestartDevice => false;

	// --- Keyboard / alerts ---

	/// <summary>
	/// Finishes typing the way a customer does, with the keyboard's confirm key, so the app sees the
	/// input completed and the keyboard goes.
	/// </summary>
	/// <param name="driver">The session whose keyboard is up.</param>
	/// <returns>
	/// Whether the input was left as well. <c>false</c> means the keyboard is down but the input still
	/// holds focus, because the platform offers no key that leaves it.
	/// </returns>
	internal abstract bool DismissKeyboard(AppiumDriver driver);

	internal abstract void DismissDefaultAlert(IAlert alert);

	internal abstract By GetAlertButtonLocator(string buttonLabel);

	/// <summary>
	/// Answers the system permission prompt the app raises on its first launch after an install, if
	/// one is showing. Must leave an alert the app itself raised alone: the first journey on a
	/// fixture may start on one. Does nothing where the session grants permissions at install.
	/// </summary>
	/// <param name="driver">The session to look for the prompt in.</param>
	/// <returns>Whether a prompt was answered.</returns>
	internal virtual bool AnswerFirstLaunchPrompt(AppiumDriver driver) => false;

	/// <summary>
	/// Clears the system's "the app is not responding" dialog, if one is showing. The dialog is the
	/// system's verdict on a frozen app rather than a screen the app drew, and it answers to the
	/// alert endpoint like any other, so a journey that meets one reports the state of the device it
	/// ran on. Does nothing on a platform whose system raises no such dialog.
	/// </summary>
	/// <param name="driver">The session to look for the dialog in.</param>
	/// <returns>Whether one was showing.</returns>
	internal virtual bool ClearNotRespondingDialog(AppiumDriver driver) => false;

	// --- Crash logs / device logs ---

	internal abstract string? ReadCrashLog(string deviceId);

	internal virtual void ClearAppLogs(string deviceId) { }

	// --- Screenshots / system UI ---

	/// <summary>
	/// Captures the whole screen from the device's side rather than through the Appium session, so
	/// system UI such as a notification banner is in it.
	/// </summary>
	/// <param name="deviceId">The device to capture.</param>
	/// <returns>The screen, in the device's own pixels.</returns>
	internal abstract Image<Rgb24> CaptureDeviceScreen(string deviceId);

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
