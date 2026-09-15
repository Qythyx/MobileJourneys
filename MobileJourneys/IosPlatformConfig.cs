using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.iOS;
using OpenQA.Selenium.Support.UI;

namespace MobileJourneys;

/// <summary>
/// iOS simulator fixture (XCUITest).
/// </summary>
/// <param name="PlatformVersion">The iOS version, e.g., "26.2".</param>
/// <param name="DeviceName">The simulator's display name visible to xcrun (e.g., "iPhone 16 Pro").</param>
/// <param name="IsLightTheme">When <c>true</c>, the system theme is forced to light before each journey.</param>
/// <param name="AppIdentifier">Bundle ID (e.g., "jp.beercats.beerbox").</param>
/// <param name="AppBinaryPath">Absolute path to the .app bundle.</param>
/// <param name="NotificationBannerTop">Where a notification banner's top edge sits on this device, in screenshot pixels.</param>
/// <param name="NotificationBannerBottom">Where a notification banner's bottom edge sits on this device, in screenshot pixels.</param>
/// <param name="ColorTolerance">Permitted per-pixel color delta (sum of R, G, B deltas) when comparing against baselines.</param>
/// <param name="MaxDiffPixelPercentage">Percentage of pixels permitted to exceed <paramref name="ColorTolerance"/> before the step fails.</param>
public sealed record IosPlatformConfig(
	string PlatformVersion,
	string DeviceName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath,
	int NotificationBannerTop,
	int NotificationBannerBottom,
	int ColorTolerance,
	double MaxDiffPixelPercentage
) : PlatformConfig(PlatformVersion, DeviceName, IsLightTheme, AppIdentifier, AppBinaryPath)
{
	/// <inheritdoc/>
	public override TestPlatform Platform => TestPlatform.iOS;

	/// <inheritdoc/>
	public override string AutomationName => "XCUITest";

	internal override void ConfigureAppiumOptions(AppiumOptions options)
	{
		options.AddAdditionalAppiumOption("simulatorStartupTimeout", SimulatorStartupTimeoutMs);
		// Xcode 27 removed Simulator.app, and this driver resolves the UI client from a hardcoded
		// path to it, so any attempt to launch one fails outright. Headless boots through simctl
		// and never touches the UI client. Removable once the driver can open Device Hub instead
		// (appium-xcuitest-driver 12+, which requires Appium 3).
		options.AddAdditionalAppiumOption("isHeadless", true);
		options.AddAdditionalAppiumOption("wdaLocalPort", FindFreePort());
		options.AddAdditionalAppiumOption("mjpegServerPort", FindFreePort());
		// Letters per second cap for XCUITest typing. Default is 60 (~17ms/key), which can drop
		// or duplicate characters; lowering to 10 (~100ms/key) gives the iOS keyboard time to
		// register each tap. (Appium's docs say "per minute" but the underlying WDA API
		// fb_typeText:frequency: is per second.)
		options.AddAdditionalAppiumOption("appium:maxTypingFrequency", 10);
	}

	internal override AppiumDriver CreateDriver(AppiumOptions options) => new IOSDriver(options);

	internal override void LaunchApp(AppiumDriver driver, IJourneyEnvironment environment, string backendUrlVariable) =>
		_ = driver.ExecuteScript(
			"mobile: launchApp",
			new Dictionary<string, object>
			{
				["bundleId"] = AppIdentifier,
				["environment"] = new Dictionary<string, string> { [backendUrlVariable] = environment.BackendUrl },
			}
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

	internal override bool DismissKeyboard(AppiumDriver driver)
	{
		// HideKeyboard() is unreliable on iOS 26+ — it may silently fail
		// or throw. Try the MAUI Done button (input accessory toolbar added by
		// MauiDoneAccessoryView for Editor/Picker controls) first, then fall
		// back to the keyboard Return key for Entry controls. Either resigns the input.
		try
		{
			driver.FindElement(By.XPath("//XCUIElementTypeToolbar//XCUIElementTypeButton")).Click();
			return true;
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

		return true;
	}

	internal override void DismissDefaultAlert(IAlert alert) =>
		// On iOS XCUITest with MAUI, Accept() maps to the cancel button
		// of a two-button DisplayAlert (the one that does nothing).
		alert.Accept();

	internal override By GetAlertButtonLocator(string buttonLabel) => By.Name(buttonLabel);

	internal override bool AnswerFirstLaunchPrompt(AppiumDriver driver)
	{
		// A system prompt belongs to SpringBoard, outside the app's element tree, so only the alert
		// endpoint sees it. The app's own alerts answer there too; the button tells them apart.
		IEnumerable<object> buttons;
		try
		{
			buttons = new WebDriverWait(driver, TimeSpan.FromSeconds(3)).Until(_ =>
			{
				try
				{
					return (IEnumerable<object>?)
						driver.ExecuteScript(
							"mobile: alert",
							new Dictionary<string, object> { ["action"] = "getButtons" }
						);
				}
				catch (WebDriverException)
				{
					return null;
				}
			});
		}
		catch (WebDriverTimeoutException)
		{
			return false;
		}

		if (!buttons.Any(button => button.ToString() == "Allow"))
		{
			return false;
		}

		_ = driver.ExecuteScript(
			"mobile: alert",
			new Dictionary<string, object> { ["action"] = "accept", ["buttonLabel"] = "Allow" }
		);
		return true;
	}

	internal override string? ReadCrashLog(string deviceId)
	{
		var containerPath = ProcessRunner
			.RunWithResult("xcrun", ["simctl", "get_app_container", deviceId, AppIdentifier, "data"])
			?.Output?.Trim();

		return string.IsNullOrWhiteSpace(containerPath) ? null : ReadCrashLogAt(containerPath);
	}

	internal static string? ReadCrashLogAt(string containerPath)
	{
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

	/// <summary>One runtime's simulators as <c>simctl list devices -j</c> reports them.</summary>
	/// <param name="Id">The runtime identifier, e.g. <c>com.apple.CoreSimulator.SimRuntime.iOS-26-2</c>.</param>
	/// <param name="ByName">The available simulators on it, keyed by name.</param>
	internal sealed record SimulatorRuntime(string Id, IReadOnlyDictionary<string, Simulator> ByName);

	/// <summary>A simulator as <c>simctl list devices -j</c> reports it.</summary>
	/// <param name="Udid">Its unique id.</param>
	/// <param name="DeviceTypeId">Its device type identifier, e.g. <c>com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro</c>.</param>
	internal sealed record Simulator(string Udid, string DeviceTypeId);

	/// <summary>Names the simulator a fixture's extra worker runs on.</summary>
	/// <param name="deviceName">The fixture's base simulator name.</param>
	/// <param name="worker">The worker's 1-based index; 2 or more.</param>
	internal static string WorkerSimulatorName(string deviceName, int worker) => $"{deviceName} · worker {worker}";

	/// <summary>
	/// Reads the runtime matching <paramref name="platformVersion"/> out of <c>simctl list devices -j</c>.
	/// </summary>
	/// <param name="simctlJson">The command's output.</param>
	/// <param name="platformVersion">The iOS version, e.g. <c>26.2</c>.</param>
	/// <returns>The runtime and its available simulators, or <c>null</c> when no runtime matches.</returns>
	internal static SimulatorRuntime? ParseSimulatorRuntime(string simctlJson, string platformVersion)
	{
		using var document = JsonDocument.Parse(simctlJson);
		var suffix = $".iOS-{platformVersion.Replace('.', '-')}";
		foreach (var runtime in document.RootElement.GetProperty("devices").EnumerateObject())
		{
			if (!runtime.Name.EndsWith(suffix, StringComparison.Ordinal))
			{
				continue;
			}

			var byName = runtime
				.Value.EnumerateArray()
				.Where(device => device.GetProperty("isAvailable").GetBoolean())
				.ToDictionary(
					device => device.GetProperty("name").GetString() ?? string.Empty,
					device => new Simulator(
						device.GetProperty("udid").GetString() ?? string.Empty,
						device.GetProperty("deviceTypeIdentifier").GetString() ?? string.Empty
					)
				);
			return new SimulatorRuntime(runtime.Name, byName);
		}

		return null;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Booting is left to the automation server, which boots a shutdown simulator it is given the
	/// UDID of. Only the simulators' existence is ensured here.
	/// </remarks>
	internal override IReadOnlyList<string> StartDevices(TimeSpan timeout)
	{
		var listing = ProcessRunner.RunWithResult("xcrun", ["simctl", "list", "devices", "available", "-j"]);
		if (listing is not { ExitCode: 0 })
		{
			throw new InvalidOperationException($"simctl could not list simulators: {listing?.Error.Trim()}");
		}

		var runtime =
			ParseSimulatorRuntime(listing.Output, PlatformVersion)
			?? throw new InvalidOperationException($"No iOS {PlatformVersion} runtime is installed.");
		if (!runtime.ByName.TryGetValue(DeviceName, out var baseDevice))
		{
			throw new InvalidOperationException($"No simulator named '{DeviceName}' exists on iOS {PlatformVersion}.");
		}

		var udids = new List<string>(Instances) { baseDevice.Udid };
		for (var worker = 2; worker <= Instances; worker++)
		{
			var name = WorkerSimulatorName(DeviceName, worker);
			udids.Add(
				runtime.ByName.TryGetValue(name, out var existing)
					? existing.Udid
					: CreateSimulator(name, baseDevice.DeviceTypeId, runtime.Id)
			);
		}

		return udids;
	}

	private static string CreateSimulator(string name, string deviceTypeId, string runtimeId)
	{
		var created = ProcessRunner.RunWithResult("xcrun", ["simctl", "create", name, deviceTypeId, runtimeId], 60);
		return created is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(created.Output)
			? created.Output.Trim()
			: throw new InvalidOperationException($"simctl could not create '{name}': {created?.Error.Trim()}");
	}

	internal override void KillStaleHelperProcesses() =>
		// The XCUITest driver's WebDriverAgent runner outlives an Appium server that goes away without
		// tearing it down. The path is unique to it, so no other xcodebuild matches. It ignores the
		// default SIGTERM, so this has to be SIGKILL to land.
		ProcessRunner.Run("pkill", ["-9", "-f", "appium-webdriveragent"]);

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

	internal static string ToIosContentSize(SystemFontSize size) =>
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
