using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
/// <param name="NotificationBannerTop">Where a notification banner's top edge sits on this device, in screenshot pixels.</param>
/// <param name="NotificationBannerBottom">Where a notification banner's bottom edge sits on this device, in screenshot pixels.</param>
/// <param name="ColorTolerance">Permitted per-pixel color delta (sum of R, G, B deltas) when comparing against baselines.</param>
/// <param name="MaxDiffPixelPercentage">Percentage of pixels permitted to exceed <paramref name="ColorTolerance"/> before the step fails.</param>
public sealed record AndroidPlatformConfig(
	string PlatformVersion,
	string DeviceName,
	string AvdName,
	bool IsLightTheme,
	string AppIdentifier,
	string AppBinaryPath,
	string? MainActivity,
	int NotificationBannerTop,
	int NotificationBannerBottom,
	int ColorTolerance,
	double MaxDiffPixelPercentage
) : PlatformConfig(PlatformVersion, DeviceName, IsLightTheme, AppIdentifier, AppBinaryPath)
{
	/// <inheritdoc/>
	public override TestPlatform Platform => TestPlatform.Android;

	/// <inheritdoc/>
	public override string AutomationName => "UiAutomator2";

	/// <summary>The activity to launch on app start, falling back to <c>$"{AppIdentifier}.MainActivity"</c>.</summary>
	public string ResolvedMainActivity => MainActivity ?? $"{AppIdentifier}.MainActivity";

	private static string AndroidHome =>
		Environment.GetEnvironmentVariable("ANDROID_HOME")
		?? throw new InvalidOperationException(MissingAndroidHomeMessage);

	private static string AdbPath => Path.Combine(AndroidHome, "platform-tools", "adb");

	private static string EmulatorPath => Path.Combine(AndroidHome, "emulator", "emulator");

	/// <summary>
	/// The system dialog's buttons, in the order they are preferred: closing the app clears the
	/// dialog for good, while waiting only asks the system for more patience with an app that has
	/// already run out of it. A given Android version may offer either.
	/// </summary>
	private static readonly string[] NotRespondingButtons = ["android:id/aerr_close", "android:id/aerr_wait"];

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
		driver.FindElement(automationId).Click();
		driver.WaitForAppToSettle(500);

		// Android MAUI Picker: opens an AlertDialog with CheckedTextView radio button items.
		driver.WaitUntil(
			WaitKind.Element,
			() => driver.App.FindElements(MobileBy.ClassName("android.widget.CheckedTextView")).Count > 0,
			"The picker's items did not appear"
		);

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
		options.AddAdditionalAppiumOption($"settings[{WaitForIdleSetting}]", WaitForIdleTimeoutMs);
		// The keyboard is its own window, and typing ends by tapping its confirm key.
		options.AddAdditionalAppiumOption("settings[enableMultiWindows]", true);
	}

	internal override AppiumDriver CreateDriver(AppiumOptions options) => new AndroidDriver(options);

	internal override void LaunchApp(AppiumDriver driver, IJourneyEnvironment environment, string backendUrlVariable)
	{
		// Android has no way to hand an app environment variables, so it travels as an intent extra.
		List<string> amArgs =
		[
			"start-activity",
			"-S",
			"-n",
			$"{AppIdentifier}/{ResolvedMainActivity}",
			"--es",
			backendUrlVariable,
			$"'{environment.BackendUrl}'",
		];

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

	internal override bool DismissKeyboard(AppiumDriver driver)
	{
		if (!driver.IsKeyboardShown())
		{
			return true;
		}

		// The keyboard's own confirm key, as on iOS, so the app sees the entry completed. Hiding the
		// keyboard from outside leaves the entry focused and its caret blinking through screenshots,
		// and the driver's editor-action command switches input methods to deliver it, which has
		// cost the window its navigation-bar inset. A multi-line editor's key only inserts a newline.
		var confirmKeys = driver.FindElements(
			By.XPath($"//*[@package!={XPathLiteral(AppIdentifier)} and (@content-desc='Done' or @content-desc='Go')]")
		);
		if (confirmKeys.Count == 0)
		{
			driver.HideKeyboard();
			return false;
		}

		confirmKeys[0].Click();
		return true;
	}

	internal override void DismissDefaultAlert(IAlert alert) => alert.Dismiss();

	/// <inheritdoc/>
	/// <remarks>
	/// The dialog is recognised by the platform's own ids for its buttons, which hold whatever
	/// language the device is set to and whichever of the buttons this Android version offers.
	/// Closing the app is the answer rather than waiting: the journey is run again on a fresh launch
	/// either way, and an app left frozen raises the dialog again over the run that follows.
	/// </remarks>
	internal override bool ClearNotRespondingDialog(AppiumDriver driver)
	{
		foreach (var button in NotRespondingButtons)
		{
			if (driver.FindElements(MobileBy.Id(button)) is [var found, ..])
			{
				found.Click();
				return true;
			}
		}

		return false;
	}

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

	/// <inheritdoc/>
	/// <remarks>
	/// Raw pixels rather than a PNG. Compressing the PNG runs on the emulated device and is most of
	/// what a capture costs, only for the host to decode it straight back into these same pixels.
	/// </remarks>
	internal override Image<Rgb24> CaptureDeviceScreen(string deviceId)
	{
		var result = ProcessRunner.RunForBytes(
			AdbPath,
			["-s", deviceId, "exec-out", "screencap"],
			ScreenCaptureTimeoutSeconds
		);
		return result is { Status: ProcessRunner.ShellResultStatus.Completed, ExitCode: 0 }
			? DecodeRawScreencap(result.Output)
			: throw new InvalidOperationException(
				$"`adb exec-out screencap` failed on {deviceId}: {result.Error.Trim()}"
			);
	}

	/// <summary>
	/// Reads the raw form of <c>screencap</c>: its width, height and pixel format as little-endian
	/// 32-bit words, then the pixels, row after row with no padding. Newer Android versions put a
	/// colour-space word after the format, so the header's length is told by what the pixels leave
	/// over, and has to be one of the two lengths Android writes.
	/// </summary>
	/// <param name="raw">Everything <c>screencap</c> wrote.</param>
	/// <returns>The screen.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the pixels are in a format other than 8-bit RGBA, or do not fit what the header says.
	/// </exception>
	internal static Image<Rgb24> DecodeRawScreencap(byte[] raw)
	{
		const int MinHeaderLength = 12;
		const int MaxHeaderLength = 16;
		const int BytesPerPixel = 4;
		if (raw.Length < MinHeaderLength)
		{
			throw new InvalidOperationException($"screencap wrote {raw.Length} bytes, too few for its header.");
		}

		var width = BinaryPrimitives.ReadInt32LittleEndian(raw);
		var height = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4));
		var format = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(8));
		if (format is not (RgbaPixelFormat or RgbxPixelFormat))
		{
			throw new InvalidOperationException($"screencap wrote pixel format {format}, which is not 8-bit RGBA.");
		}

		var pixelLength = width * height * BytesPerPixel;
		var headerLength = raw.Length - pixelLength;
		if (headerLength is not (MinHeaderLength or MaxHeaderLength))
		{
			throw new InvalidOperationException(
				$"screencap wrote {raw.Length} bytes, which is not a {width}x{height} screen behind either header it writes."
			);
		}

		using var screen = Image.LoadPixelData<Rgba32>(raw.AsSpan(headerLength, pixelLength), width, height);
		return screen.CloneAs<Rgb24>();
	}

	/// <summary>Android's <c>PIXEL_FORMAT_RGBA_8888</c>.</summary>
	private const int RgbaPixelFormat = 1;

	/// <summary>Android's <c>PIXEL_FORMAT_RGBX_8888</c>: the same layout, its fourth byte unused.</summary>
	private const int RgbxPixelFormat = 2;

	/// <summary>
	/// How long one capture may take. It is a few hundred milliseconds on an idle host, and several
	/// seconds on one driving too many devices.
	/// </summary>
	private const int ScreenCaptureTimeoutSeconds = 15;

	internal override int GetStatusBarHeight(AppiumDriver driver) =>
		driver.GetDict("mobile: getSystemBars").GetDict("statusBar").GetInt("height");

	internal override int GetHomeIndicatorHeight(AppiumDriver driver) =>
		driver.GetDict("mobile: getSystemBars").GetDict("navigationBar").GetInt("height");

	internal override void SetSystemTheme(string deviceId, bool isLightTheme) =>
		ProcessRunner.Run(AdbPath, ["-s", deviceId, "shell", "cmd", "uimode", "night", isLightTheme ? "no" : "yes"]);

	internal override void SetSystemFontSize(string deviceId, SystemFontSize size)
	{
		var scale = ToAndroidFontScale(size).ToString(CultureInfo.InvariantCulture);
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

		// The spell checker underlines a typed test value it does not know, some time after the
		// keyboard has gone, so whether a screenshot carries the underline is a race.
		_ = RunAdb(deviceId, "shell", "settings", "put", "secure", "spell_checker_enabled", "0");
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

	/// <inheritdoc/>
	public override void StartForwardingPort(string deviceId, int port) =>
		RunAdbOrThrow(deviceId, "reverse", $"tcp:{port}", $"tcp:{port}");

	/// <inheritdoc/>
	/// <remarks>
	/// adb treats an already-absent listener as a failure; here it is the intended end state.
	/// </remarks>
	public override void StopForwardingPort(string deviceId, int port) =>
		_ = RunAdb(deviceId, "reverse", "--remove", $"tcp:{port}");

	/// <inheritdoc/>
	/// <remarks>
	/// Instances already running are kept while they are fresh, and all of them are stopped and
	/// booted again once any has been up longer than <see cref="MaxInstanceAge"/>. An emulator gets
	/// slower the longer it runs, several times over within a few days, and a boot from its
	/// quick-boot snapshot takes seconds. Instances are killed rather than stopped gracefully, since a
	/// writable instance stopped gracefully saves its snapshot, and the boot would load the very state
	/// it was stopped to escape. They are found in the process table, which cannot block on one that
	/// is wedged.
	/// <para/>
	/// An AVD may be shared only by instances that all run read-only: the emulator refuses to start a
	/// read-only instance beside a writable one, so a writable instance is stopped too when the
	/// fixture wants several. A lone instance runs writable.
	/// </remarks>
	internal override IReadOnlyList<string> StartDevices(TimeSpan timeout, CancellationToken cancellationToken)
	{
		var deadline = DateTime.UtcNow + timeout;
		var running = RunningInstances();
		if (running.Any(MustRestart))
		{
			foreach (var instance in running)
			{
				Kill(PidOf(instance));
			}

			while (RunningInstances().Count > 0)
			{
				if (DateTime.UtcNow >= deadline)
				{
					throw new InvalidOperationException(
						$"the running instances of {AvdName} did not stop within {timeout.TotalSeconds}s."
					);
				}

				cancellationToken.Sleep(BootPollInterval);
			}

			running = [];
		}

		for (var instance = running.Count; instance < Instances; instance++)
		{
			ProcessRunner.Start(EmulatorPath, EmulatorArguments(Instances > 1));
		}

		while (true)
		{
			var serials = AttachedDevices().Where(serial => AvdNameOf(serial) == AvdName).Take(Instances).ToList();
			if (serials.Count == Instances)
			{
				return serials;
			}

			if (DateTime.UtcNow >= deadline)
			{
				throw new InvalidOperationException(
					$"only {serials.Count} of {Instances} instances of {AvdName} appeared within {timeout.TotalSeconds}s."
				);
			}

			KickOfflineDevices();
			cancellationToken.Sleep(BootPollInterval);
		}
	}

	/// <summary>
	/// How long an instance may have been up and still be kept for a run. Measured, instances a few
	/// hours old launched apps as fast as a fresh boot, and ones a few days old six times slower; the
	/// margin is wide because a restart costs well under a minute.
	/// </summary>
	private static readonly TimeSpan MaxInstanceAge = TimeSpan.FromHours(1);

	/// <summary>
	/// Whether a running instance cannot be kept for this run: it has been up long enough to have
	/// slowed, or its age cannot be told, or it runs writable where the fixture wants read-only
	/// instances.
	/// </summary>
	/// <param name="instance">The instance, as <see cref="RunningInstances"/> lists it.</param>
	/// <returns>Whether it has to be restarted.</returns>
	private bool MustRestart(string instance) =>
		(Instances > 1 && !instance.Contains(ReadOnlyFlag, StringComparison.Ordinal))
		|| UptimeOf(PidOf(instance)) is not { } uptime
		|| uptime > MaxInstanceAge;

	/// <summary>How long a process has been running.</summary>
	/// <param name="pid">The process.</param>
	/// <returns>Its uptime, or <c>null</c> when it cannot be told, as when it has already exited.</returns>
	internal static TimeSpan? UptimeOf(int pid)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			return DateTime.Now - process.StartTime;
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>
	/// Kills a process outright, giving it no chance to save anything on the way down. A process that
	/// has already exited, or cannot be killed, is left to the caller's wait for it to be gone.
	/// </summary>
	/// <param name="pid">The process.</param>
	internal static void Kill(int pid)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			process.Kill();
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
	}

	private static int PidOf(string instance) => int.Parse(instance.Split(' ')[0], CultureInfo.InvariantCulture);

	private const string ReadOnlyFlag = "-read-only";

	private string[] EmulatorArguments(bool readOnly) => readOnly ? ["-avd", AvdName, ReadOnlyFlag] : ["-avd", AvdName];

	internal override bool CanRestartDevice => true;

	/// <inheritdoc/>
	/// <remarks>
	/// The instance is killed outright: stopped gracefully, a writable instance saves its quick-boot
	/// snapshot, and the fresh boot would load the very state it was restarted to escape. Its process
	/// is found from the console port its serial names, without asking the device, and it comes back
	/// on that same port so the serial holds. It comes back read-only exactly when it ran read-only,
	/// since the emulator refuses to mix the two among one AVD's instances.
	/// </remarks>
	internal override bool RestartDevice(string deviceId, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		var consolePort = deviceId[EmulatorSerialPrefix.Length..];
		// lsof exits 1 with nothing printed when no process listens on the port.
		if (
			ProcessRunner.RunWithResult(LsofPath, ["-nP", $"-iTCP:{consolePort}", "-sTCP:LISTEN", "-t"])
			is not { Status: ProcessRunner.ShellResultStatus.Completed } listener
		)
		{
			return false;
		}

		var readOnly = Instances > 1;
		var pid = listener.Output.Trim();
		if (pid.Length > 0)
		{
			if (InstanceListeningOn(pid, RunningInstances()) is not { } instance)
			{
				return false;
			}

			readOnly = instance.Contains(ReadOnlyFlag, StringComparison.Ordinal);
			Kill(PidOf(instance));
			while (InstanceListeningOn(pid, RunningInstances()) is not null)
			{
				if (DateTime.UtcNow >= deadline)
				{
					return false;
				}

				// Not cut short by an interrupted run: the device is on its way down, and bringing it
				// back is what spares the next run a cold start.
				Thread.Sleep(BootPollInterval);
			}
		}

		ProcessRunner.Start(EmulatorPath, [.. EmulatorArguments(readOnly), "-port", consolePort]);
		return true;
	}

	/// <summary>
	/// Picks the running instance a console port's listener belongs to, so that a port some other
	/// process has taken since is never what gets killed.
	/// </summary>
	/// <param name="listenerPids">What <c>lsof -t</c> printed for the port: one process id per line.</param>
	/// <param name="runningInstances">This AVD's instances, as <c>pgrep -fl</c> prints them.</param>
	/// <returns>The instance's command line, or <c>null</c> unless the listener is exactly one of them.</returns>
	internal static string? InstanceListeningOn(string listenerPids, IEnumerable<string> runningInstances) =>
		listenerPids.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var pid]
			? runningInstances.FirstOrDefault(commandLine =>
				commandLine.StartsWith($"{pid} ", StringComparison.Ordinal)
			)
			: null;

	private const string EmulatorSerialPrefix = "emulator-";

	private const string LsofPath = "/usr/sbin/lsof";

	/// <summary>The process ids and command lines of this AVD's running emulator instances.</summary>
	private List<string> RunningInstances() =>
		// Anchored on a separator so one AVD is not matched by another that extends its name.
		ProcessRunner.RunWithResult(PgrepPath, ["-fl", $"qemu-system.*-avd {AvdName}( |$)"])
			is { ExitCode: 0 } found
			? [.. found.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)]
			: [];

	private const string PgrepPath = "/usr/bin/pgrep";

	private static string AvdNameOf(string serial) => GetProperty(serial, "ro.boot.qemu.avd_name");

	/// <inheritdoc/>
	/// <remarks>
	/// An emulator answers <c>adb devices</c> well before Android is usable: the package manager is
	/// still settling, and a session started in that window fails installing or launching a helper
	/// app — most visibly as <c>Activity class {io.appium.settings/…} does not exist</c>. Waiting for
	/// the boot to actually complete closes that window. A device starved off its transport by a
	/// loaded host reports <c>device offline</c> and is waited out here too.
	/// <para/>
	/// A device long past its boot can pass every one of those checks and still not start an
	/// activity, so readiness ends with starting the home screen — the kind of call a session opens
	/// with, given the time a session gives it. One that times out has stopped answering, and waiting
	/// longer does not bring it back.
	/// </remarks>
	internal override DeviceReadiness WaitUntilDeviceIsReady(
		string deviceId,
		TimeSpan timeout,
		CancellationToken cancellationToken
	)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			var home = IsBootComplete(deviceId)
				? ProcessRunner.RunWithResult(
					AdbPath,
					[
						"-s",
						deviceId,
						"shell",
						"am",
						"start",
						"-W",
						"-a",
						"android.intent.action.MAIN",
						"-c",
						"android.intent.category.HOME",
					],
					AppWaitDurationMs / 1000
				)
				: null;
			if (home is { ExitCode: 0 })
			{
				return DeviceReadiness.Ready;
			}

			if (home is { Status: ProcessRunner.ShellResultStatus.TimedOut })
			{
				return DeviceReadiness.Unresponsive;
			}

			KickOfflineDevices();
			cancellationToken.Sleep(BootPollInterval);
		}

		return DeviceReadiness.Booting;
	}

	/// <summary>
	/// adb keeps a device it dropped to "offline" there until the host kicks the transport, so
	/// polling alone never gets it back. A no-op when nothing is offline.
	/// </summary>
	private static void KickOfflineDevices() => _ = ProcessRunner.RunWithResult(AdbPath, ["reconnect", "offline"]);

	private static readonly TimeSpan BootPollInterval = TimeSpan.FromSeconds(2);

	private static bool IsBootComplete(string deviceId) =>
		GetProperty(deviceId, "sys.boot_completed") == "1"
		&& GetProperty(deviceId, "init.svc.bootanim") == "stopped"
		// The package manager is the part that is not ready yet when the boot flags already say it is.
		&& RunAdb(deviceId, "shell", "pm", "path", "android") is { ExitCode: 0 };

	private static string GetProperty(string deviceId, string name) =>
		RunAdb(deviceId, "shell", "getprop", name) is { ExitCode: 0 } result ? result.Output.Trim() : string.Empty;

	private static IEnumerable<string> AttachedDevices() =>
		ProcessRunner.RunWithResult(AdbPath, ["devices"]) is { ExitCode: 0 } result
			? ParseAttachedDevices(result.Output)
			: [];

	/// <summary>
	/// Reads the device serials out of <c>adb devices</c> output, skipping its header and any device
	/// in a state that will never become usable on its own (<c>unauthorized</c>, <c>no permissions</c>).
	/// </summary>
	/// <param name="adbDevicesOutput">Standard output of <c>adb devices</c>.</param>
	internal static IEnumerable<string> ParseAttachedDevices(string adbDevicesOutput) =>
		adbDevicesOutput
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
			// A device still coming up reports "offline", which is exactly what this waits out.
			.Where(parts => parts is [_, "device" or "offline"])
			.Select(parts => parts[0].Trim());

	private static void RunAdbOrThrow(string deviceId, params string[] arguments)
	{
		var result = RunAdb(deviceId, arguments);
		if (result is null || result.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"adb {string.Join(' ', arguments)} failed: {result?.Error.Trim() ?? "adb could not be started."}"
			);
		}
	}

	private static ProcessRunner.ShellResult<string>? RunAdb(string deviceId, params string[] arguments)
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
