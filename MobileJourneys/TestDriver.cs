using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;

namespace MobileJourneys;

/// <summary>
/// High-level wrapper around <see cref="AppiumDriver"/> that powers journey actions and
/// expectations: element finding (with retry), gestures (taps, swipes), keyboard,
/// alerts, deep links, screenshot stability polling, and crash detection.
/// </summary>
/// <param name="app">The underlying Appium driver.</param>
/// <param name="config">Platform fixture (drives platform-specific branches).</param>
/// <param name="screenshotManager">Instance used for baseline capture and comparison.</param>
/// <param name="backendUrlVariable">The name the app reads the backend's address under.</param>
public sealed class TestDriver(
	AppiumDriver app,
	PlatformConfig config,
	ScreenshotManager screenshotManager,
	string backendUrlVariable
)
{
	/// <summary>
	/// AutomationId the consumer's app should set on the content view immediately
	/// below the notification banner. Used by the framework to mask out the banner
	/// region when comparing screenshots.
	/// </summary>
	public const string AutomationIdBelowNotification = "ScreenBelowNotification";

	/// <summary>
	/// How long to wait for a mask element while resolving a step's mask regions. Masks are resolved
	/// around the step's action, so the element is expected to be on screen already; the wait only
	/// absorbs render lag rather than allowing for one to appear later.
	/// </summary>
	private static readonly TimeSpan MaskLookupTimeout = TimeSpan.FromSeconds(5);

	/// <summary>How long <see cref="CaptureEmptyBannerRegion"/> waits for its region to stop changing.</summary>
	private static readonly TimeSpan EmptyBannerRegionSettleTimeout = TimeSpan.FromSeconds(10);

	/// <summary>The underlying Appium driver.</summary>
	public AppiumDriver App { get; } = app;

	/// <summary>The platform fixture this driver is bound to.</summary>
	public PlatformConfig Config { get; } = config;

	/// <summary>
	/// The current journey's environment. Populated by <c>JourneyRunner</c> before each
	/// journey runs; consumer-side actions read this to derive fixture-aware behavior
	/// (e.g., picking the right localized label).
	/// </summary>
	public IJourneyEnvironment CurrentJourneyEnv { get; set; } = null!;

	/// <summary>
	/// The stand-in backend this fixture's app talks to, or <c>null</c> when the suite declares none.
	/// Set by <c>SuiteRunner</c> once the fixture is up; consumer-side actions reach through it to
	/// drive backend state part-way through a journey.
	/// </summary>
	public IJourneyBackend? Backend { get; set; }

	private Rectangle[] SystemMasks =>
		field ??= [
			.. new Rectangle[] { GetStatusBarMask(), GetHomeIndicatorMask() }.Where(rect =>
				rect.Width > 0 && rect.Height > 0
			),
		];

	/// <summary>
	/// Screenshot pixels per viewport unit, measured once per session. Positions taken off a
	/// screenshot and positions handed to a tap are in different spaces on a device that renders
	/// above 1x, so one has to be converted into the other. Measured rather than assumed per
	/// platform, since the two spaces coincide on some devices and not on others.
	/// <para/>
	/// Any comparison the session has already made fills this in; measuring it here instead costs a
	/// capture, which a caller racing a banner that dismisses itself cannot spare.
	/// </summary>
	private double _screenScale;

	private void RecordScreenScale(int imageHeight, int windowHeight) =>
		_screenScale = (double)imageHeight / windowHeight;

	/// <summary>
	/// When set, <see cref="DoActionAndCompareWithBaseline"/> uses these bytes instead of
	/// taking an Appium screenshot. Cleared after use. Used for device-level screenshots
	/// (e.g., native notification banners captured via <c>xcrun simctl io</c>).
	/// </summary>
	private byte[]? _screenshotPngBytes;

	private Image<Rgb24>? _emptyBannerRegion;

	/// <summary>Returns the device UDID/transport-ID this driver is connected to.</summary>
	public string GetDeviceId()
	{
		var capabilityName = Config.DeviceIdCapabilityName;
		return App.Capabilities.GetCapability(capabilityName)?.ToString()
			?? throw new InvalidOperationException(
				$"Could not get device ID from driver capability '{capabilityName}'"
			);
	}

	/// <summary>Terminates the app and relaunches it with fresh environment variables.</summary>
	/// <param name="environment">Per-journey mock state to pass as env vars.</param>
	public void RelaunchApp(IJourneyEnvironment environment)
	{
		Config.TerminateApp(App);
		// The app is now running this environment, so steps deriving their next one from
		// CurrentJourneyEnv build on it rather than on whatever the journey started with.
		CurrentJourneyEnv = environment;
		Backend?.ServeEnvironment(environment);
		Config.LaunchApp(App, environment, backendUrlVariable);
		WaitUntilAppIsSettled();

		// A permission prompt the app raises on first launch covers the screen, so the first journey on
		// a freshly installed fixture fails on every step behind it. Answered once per fixture, because
		// the answer sticks for the install and this wait costs its full timeout when nothing appears.
		if (!_firstLaunchAlertHandled)
		{
			_firstLaunchAlertHandled = true;
			DismissAlertIfPresent(TimeSpan.FromSeconds(3));
		}
	}

	private bool _firstLaunchAlertHandled;

	/// <summary>
	/// Blocks until the freshly launched app is in the foreground and its accessibility tree has
	/// stopped changing, or until a fixed timeout elapses. Without this, a slow cold start spends the
	/// first expectation's timeout budget rather than the launch's, so a launch that is merely slow
	/// reads as an element that is missing.
	/// </summary>
	private void WaitUntilAppIsSettled()
	{
		const int SettleTimeoutMs = 30000;
		const int PollIntervalMs = 250;
		const long ForegroundState = 4;

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		string? previousTree = null;
		while (stopwatch.ElapsedMilliseconds < SettleTimeoutMs)
		{
			try
			{
				if (Config.QueryAppState(App) == ForegroundState)
				{
					// Two identical reads mean the UI has stopped rebuilding (splash → content).
					var tree = App.PageSource;
					if (tree == previousTree)
					{
						return;
					}

					previousTree = tree;
				}
			}
			catch (WebDriverException)
			{
				// The app or its accessibility tree is not queryable yet; keep waiting.
				previousTree = null;
			}

			Task.Delay(PollIntervalMs).Wait();
		}
	}

	/// <summary>
	/// Captures a screenshot for failure diagnostics, returning an empty array when the app is
	/// unreachable (a crash, or a session that has already gone away).
	/// </summary>
	public byte[] TryCaptureScreenshot()
	{
		try
		{
			return App.GetScreenshot().AsByteArray;
		}
		catch (Exception ex) when (ex is WebDriverException or InvalidOperationException)
		{
			return [];
		}
	}

	/// <summary>Polls until the element with the given AutomationId is no longer present or the timeout elapses.</summary>
	public void WaitForElementNotFound(string automationId, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				_ = FindElementNow(automationId);
				Thread.Sleep(250);
			}
			catch (NoSuchElementException)
			{
				return;
			}
		}

		throw new TimeoutException($"Element '{automationId}' still present after {timeout.TotalSeconds}s");
	}

	/// <summary>
	/// Waits for an element with the given AutomationId to be visible, optionally verifying an
	/// additional condition.
	/// </summary>
	/// <param name="automationId">The AutomationId of the element.</param>
	/// <param name="timeout">Maximum time to wait.</param>
	/// <param name="condition">
	/// Optional tuple of (predicate, messageFactory). The predicate is evaluated once the element is
	/// found; if it returns false, polling continues. On timeout, messageFactory receives the last
	/// element seen (or null if never found) and should return a description of what failed.
	/// </param>
	/// <returns>The found element.</returns>
	/// <exception cref="TimeoutException">
	/// Thrown when the element is not found before the timeout, or when a
	/// <paramref name="condition"/> is supplied and its predicate never returns true.
	/// </exception>
	public AppiumElement FindElement(
		string automationId,
		TimeSpan timeout,
		(Func<AppiumElement, bool> check, Func<AppiumElement?, string> message)? condition = null
	)
	{
		var wait = new WebDriverWait(App, timeout);
		wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
		AppiumElement? lastElement = null;

		try
		{
			return wait.Until(_ =>
			{
				var element = FindElementNow(automationId);

				if (condition is not { } c || c.check(element))
				{
					return element;
				}

				lastElement = element;
				return null;
			});
		}
		catch (WebDriverTimeoutException) when (condition is { } c && lastElement is not null)
		{
			throw new TimeoutException(
				$"Element '{automationId}' {c.message(lastElement)} after {timeout.TotalSeconds}s"
			);
		}
		catch (WebDriverTimeoutException)
		{
			throw new TimeoutException($"Element '{automationId}' not found after {timeout.TotalSeconds}s.");
		}
	}

	/// <summary>
	/// Locates an element by AutomationId once, without waiting. Tries resource-id first, then falls
	/// back to content-desc (accessibility ID): on Android, MAUI maps AutomationId to resource-id for
	/// most elements, but Shell.ItemTemplate bindings map to content-desc instead. Every lookup goes
	/// through here so none of them sees only half the elements.
	/// </summary>
	/// <param name="automationId">The AutomationId of the element.</param>
	/// <returns>The found element.</returns>
	/// <exception cref="NoSuchElementException">Thrown when neither locator matches.</exception>
	private AppiumElement FindElementNow(string automationId)
	{
		try
		{
			return (AppiumElement)App.FindElement(MobileBy.Id(automationId));
		}
		catch (NoSuchElementException)
		{
			return (AppiumElement)App.FindElement(MobileBy.AccessibilityId(automationId));
		}
	}

	/// <summary>
	/// Waits for an element with the given AutomationId to be visible and contain one of the
	/// expected text values (case-insensitive).
	/// </summary>
	/// <param name="automationId">The AutomationId of the element.</param>
	/// <param name="expectedTexts">One or more text values the element may contain (any match succeeds).</param>
	/// <param name="timeout">Maximum time to wait.</param>
	/// <returns>The found element.</returns>
	/// <exception cref="TimeoutException">
	/// Thrown when the element is not found within the timeout, or is found but does not contain
	/// any of the expected texts.
	/// </exception>
	public AppiumElement FindElementWithText(string automationId, string[] expectedTexts, TimeSpan timeout) =>
		FindElement(
			automationId,
			timeout,
			(
				element => expectedTexts.Any(t => element.Text.Contains(t, StringComparison.OrdinalIgnoreCase)),
				element =>
					$"did not contain any of [{string.Join(", ", expectedTexts)}]. Actual text: '{element?.Text}'"
			)
		);

	/// <summary>Sleeps for the given duration to let UI animations / async work settle.</summary>
	public static void WaitForAppToSettle(int milliseconds) => Task.Delay(milliseconds).Wait();

	/// <summary>
	/// Polls screenshots until one matches the step's baseline (skipping masked regions), then
	/// compares it. A screen that is still arriving — an image that has not finished decoding, a
	/// page still transitioning — holds as still as a finished one, so stability alone stops the
	/// polling too early; the baseline is what finished looks like. Falls back to comparing the
	/// last capture once the budget runs out, so a step that really does differ still fails on its
	/// screenshot, showing what it caught.
	/// </summary>
	/// <param name="action">The test action to run.</param>
	/// <param name="testStep">Identifies the step whose baseline the stable screenshot is compared against.</param>
	/// <param name="maskElementIds">AutomationIds of elements to exclude from stability polling and baseline comparison.</param>
	/// <param name="prefetchMasks"><c>true</c> to query the mask element IDs before running the
	/// test action. This is useful if the action will cause the elements to become unavailable
	/// such as showing a alert above them.</param>
	public ScreenshotComparisonResult DoActionAndCompareWithBaseline(
		Action action,
		TestStep testStep,
		string[] maskElementIds,
		bool prefetchMasks
	)
	{
		if (!prefetchMasks)
		{
			action();
		}

		var windowSize = App.Manage().Window.Size;
		// Measured before the capture, not after it. A step that masks an element is usually masking
		// one that only exists while the action's work is in flight, and a screenshot is a slow round
		// trip — taking it first spends the element's whole lifetime before anyone looks for it.
		var maskElements = maskElementIds
			.Where(id => id != AutomationIdBelowNotification)
			.Select(GetElementRectangle)
			.ToArray();
		var masksBelowNotification = maskElementIds.Contains(AutomationIdBelowNotification);

		Rectangle[] MaskRegionsFor(Image image)
		{
			RecordScreenScale(image.Height, windowSize.Height);
			var scaled = ImageHelpers.ScaleMaskRegions(
				[.. maskElements.Concat(SystemMasks)],
				windowSize,
				new System.Drawing.Size(image.Width, image.Height)
			);

			// The banner's bounds are configured in the captured image's own pixels, so this region
			// joins the others only once they have been scaled into that same space.
			return masksBelowNotification ? [.. scaled, BelowNotificationMask(image)] : scaled;
		}

		if (prefetchMasks)
		{
			action();
		}

		// If a device-level screenshot was captured (e.g., native notification banner),
		// use it directly instead of polling Appium.
		if (_screenshotPngBytes is not null)
		{
			var captured = Image.Load<Rgb24>(_screenshotPngBytes);
			_screenshotPngBytes = null;
			return screenshotManager.CompareWithBaselineAndDispose(captured, testStep, MaskRegionsFor(captured));
		}

		// A capture costs a second or two on a loaded device, so this is a budget for a handful of
		// samples, not for many. Only a step that never matches spends all of it.
		const int MaxWaitMs = 10000;
		const int MinMsBetweenScreenshots = 300;

		// With nothing to match against, two identical captures are the only signal there is, and the
		// first run records whichever one settles as the baseline every later run is held to.
		using var baseline = screenshotManager.TryLoadBaseline(testStep);
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		Image<Rgb24>? previousImage = null;
		var maskRegions = Array.Empty<Rectangle>();
		var elapsed = stopwatch.Elapsed;
		while (stopwatch.Elapsed.TotalMilliseconds < MaxWaitMs)
		{
			var screenshot = App.GetScreenshot().AsImage();
			if (previousImage is not null && (stopwatch.Elapsed - elapsed).TotalMilliseconds < MinMsBetweenScreenshots)
			{
				screenshot.Dispose();
				Task.Delay(100).Wait();
				continue;
			}
			elapsed = stopwatch.Elapsed;
			maskRegions = previousImage is null ? MaskRegionsFor(screenshot) : maskRegions;

			var done = baseline is not null
				? baseline.Matches(screenshot, maskRegions)
				: previousImage is not null && ImageHelpers.AreImagesEqual(screenshot, previousImage, maskRegions);
			if (done)
			{
				previousImage?.Dispose();
				return screenshotManager.CompareWithBaselineAndDispose(screenshot, testStep, maskRegions);
			}

			previousImage?.Dispose();
			previousImage = screenshot;
		}

		return screenshotManager.CompareWithBaselineAndDispose(previousImage!, testStep, maskRegions);
	}

	private Rectangle GetElementRectangle(string automationId)
	{
		var element = FindElement(automationId, MaskLookupTimeout);
		return new(element.Location.X, element.Location.Y, element.Size.Width, element.Size.Height);
	}

	/// <summary>Everything under a notification banner, in the captured image's pixels.</summary>
	/// <param name="image">The capture the region is being masked out of.</param>
	/// <returns>The region spanning the full width from the banner's bottom edge down.</returns>
	private Rectangle BelowNotificationMask(Image image) =>
		new(0, Config.NotificationBannerBottom, image.Width, image.Height - Config.NotificationBannerBottom);

	/// <summary>Performs a single-finger swipe from (startX, startY) to (endX, endY) in viewport coordinates.</summary>
	public void SwipeScreen(int startX, int startY, int endX, int endY)
	{
		var finger = new PointerInputDevice(PointerKind.Touch, "finger");
		var sequence = new ActionSequence(finger, 0);

		_ = sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, startX, startY, TimeSpan.Zero));
		_ = sequence.AddAction(finger.CreatePointerDown(MouseButton.Left));
		_ = sequence.AddAction(
			finger.CreatePointerMove(CoordinateOrigin.Viewport, endX, endY, TimeSpan.FromMilliseconds(300))
		);
		_ = sequence.AddAction(finger.CreatePointerUp(MouseButton.Left));

		App.PerformActions([sequence]);
	}

	/// <summary>Swipes left on the given scrollable element.</summary>
	public void SwipeLeft(string onElementId) => SwipeOnElement(onElementId, "left");

	/// <summary>Swipes right on the given scrollable element.</summary>
	public void SwipeRight(string onElementId) => SwipeOnElement(onElementId, "right");

	/// <summary>Dismisses the on-screen keyboard if present.</summary>
	public void DismissKeyboard() => Config.DismissKeyboard(App);

	/// <summary>Opens an in-app deep link via mobile: deepLink (Android) or App.OpenUrl (iOS).</summary>
	public void OpenDeepLink(string url) => Config.OpenDeepLink(App, url);

	private void SwipeOnElement(string automationId, string direction)
	{
		// Use a gesture-based swipe constrained to the element's bounds. This is more reliable
		// than mobile: swipeGesture/swipe which can behave inconsistently with MAUI views.
		// Target the upper third of the element to reliably hit the scrollable area (e.g., the
		// image portion of a carousel item rather than the text details below).
		var element = FindElement(automationId, TimeSpan.FromSeconds(5));
		var location = element.Location;
		var size = element.Size;
		var centerY = location.Y + (int)(size.Height * 0.35);

		int startX;
		int endX;
		if (direction == "left")
		{
			startX = location.X + (int)(size.Width * 0.8);
			endX = location.X + (int)(size.Width * 0.2);
		}
		else
		{
			startX = location.X + (int)(size.Width * 0.2);
			endX = location.X + (int)(size.Width * 0.8);
		}

		SwipeScreen(startX, centerY, endX, centerY);
	}

	/// <summary>Dismisses any visible alert; no-op when none is shown.</summary>
	public void DismissAlertIfPresent(TimeSpan? timeout = null)
	{
		try
		{
			_ = new WebDriverWait(App, timeout ?? TimeSpan.FromSeconds(5)).Until(driver =>
			{
				try
				{
					_ = driver.SwitchTo().Alert();
					return true;
				}
				catch (NoAlertPresentException)
				{
					return false;
				}
			});

			Config.DismissDefaultAlert(App.SwitchTo().Alert());

			// Allow the alert dismissal animation to complete before the next action.
			WaitForAppToSettle(300);
		}
		catch (WebDriverTimeoutException)
		{
			// No alert appeared within timeout
		}
	}

	/// <summary>
	/// Finds and taps a specific button within a visible alert/dialog by its label text.
	/// This is more reliable than Selenium's <c>alert.Accept()</c>/<c>alert.Dismiss()</c>,
	/// which have inconsistent platform mappings for two-button MAUI alerts.
	/// </summary>
	/// <param name="buttonLabel">The exact text of the button to tap.</param>
	public void TapAlertButton(string buttonLabel)
	{
		try
		{
			App.FindElement(Config.GetAlertButtonLocator(buttonLabel)).Click();
		}
		catch (NoSuchElementException)
		{
			throw new InvalidOperationException($"Alert button with label '{buttonLabel}' not found.");
		}

		WaitForAppToSettle(300);
	}

	/// <summary>Waits for a system alert to appear within the timeout.</summary>
	public void WaitForAlert(TimeSpan timeout) =>
		_ = new WebDriverWait(App, timeout).Until(driver =>
		{
			try
			{
				_ = driver.SwitchTo().Alert();
				return true;
			}
			catch (NoAlertPresentException)
			{
				return false;
			}
		});

	/// <summary>
	/// Polls until the banner's region stops changing, and keeps that as what
	/// <see cref="WaitForNotificationBanner"/> compares against.
	/// <para/>
	/// Must run before the notification is scheduled. Captured with the banner already up, it
	/// describes the banner as the background, and the wait can no longer see it.
	/// </summary>
	public void CaptureEmptyBannerRegion()
	{
		_emptyBannerRegion?.Dispose();
		_emptyBannerRegion = null;

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		var previous = CropToBannerRegion(CaptureDeviceScreenshotBytes());
		while (stopwatch.Elapsed < EmptyBannerRegionSettleTimeout)
		{
			var current = CropToBannerRegion(CaptureDeviceScreenshotBytes());
			if (ImageHelpers.AreImagesEqual(current, previous, []))
			{
				previous.Dispose();
				_emptyBannerRegion = current;
				return;
			}

			previous.Dispose();
			previous = current;
		}

		// Never settled — the wait's own stability check still has to agree with whatever this is.
		_emptyBannerRegion = previous;
	}

	/// <summary>
	/// Waits for a notification banner by polling the banner's region until it both differs from
	/// <see cref="CaptureEmptyBannerRegion"/>'s capture and has stopped changing — so a banner already
	/// on screen when the wait starts counts, rather than being taken for the background. Leaves the
	/// winning screenshot for the step's baseline comparison.
	/// </summary>
	/// <param name="timeout">Maximum time to wait.</param>
	/// <exception cref="InvalidOperationException">Thrown when no empty region was captured.</exception>
	/// <exception cref="TimeoutException">Thrown when no banner appears within the timeout.</exception>
	public void WaitForNotificationBanner(TimeSpan timeout)
	{
		// Consumed here, so a second wait cannot quietly measure against an earlier step's screen.
		using var emptyRegion =
			_emptyBannerRegion
			?? throw new InvalidOperationException(
				$"{nameof(CaptureEmptyBannerRegion)} has to run before the notification is "
					+ "scheduled, so this wait has a banner-free view of the region to compare against."
			);
		_emptyBannerRegion = null;

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		var previous = CropToBannerRegion(CaptureDeviceScreenshotBytes());

		while (stopwatch.Elapsed < timeout)
		{
			var currentBytes = CaptureDeviceScreenshotBytes();
			var current = CropToBannerRegion(currentBytes);
			var showsBanner = !ImageHelpers.AreImagesEqual(current, emptyRegion, []);
			if (showsBanner && ImageHelpers.AreImagesEqual(current, previous, []))
			{
				previous.Dispose();
				current.Dispose();
				_screenshotPngBytes = currentBytes;
				return;
			}

			previous.Dispose();
			previous = current;
		}

		previous.Dispose();
		throw new TimeoutException(
			$"No notification banner appeared over rows {Config.NotificationBannerTop}-"
				+ $"{Config.NotificationBannerBottom} within {timeout.TotalSeconds}s"
		);
	}

	/// <summary>Crops a full-screen PNG to the rows this fixture's notification banner occupies.</summary>
	/// <param name="pngBytes">Raw PNG bytes of the full screenshot.</param>
	/// <returns>The banner's rows of that screenshot.</returns>
	private Image<Rgb24> CropToBannerRegion(byte[] pngBytes)
	{
		var image = Image.Load<Rgb24>(pngBytes);
		var top = Math.Clamp(Config.NotificationBannerTop, 0, image.Height);
		var bottom = Math.Clamp(Config.NotificationBannerBottom, top, image.Height);
		image.Mutate(x => x.Crop(new SixLabors.ImageSharp.Rectangle(0, top, image.Width, bottom - top)));
		return image;
	}

	/// <summary>
	/// Captures a device-level screenshot and returns the raw PNG bytes.
	/// </summary>
	private byte[] CaptureDeviceScreenshotBytes()
	{
		var tmpPath = Path.Combine(Path.GetTempPath(), $"device_screenshot_{Guid.NewGuid():N}.png");
		try
		{
			Config.CaptureDeviceScreenshot(GetDeviceId(), tmpPath);
			return File.ReadAllBytes(tmpPath);
		}
		finally
		{
			File.Delete(tmpPath);
		}
	}

	/// <summary>
	/// Checks whether the app process has exited (actual crash) using Appium's queryAppState.
	/// Returns true if the app is not running; false if it's still alive.
	/// </summary>
	public bool IsAppCrashed()
	{
		try
		{
			// queryAppState returns: 0 = not installed, 1 = not running, 3 = background, 4 = foreground
			return Config.QueryAppState(App) <= 1;
		}
		catch
		{
			// If we can't query the app state, assume it crashed
			return true;
		}
	}

	/// <summary>
	/// Retrieves the crash log written by the app's unhandled exception handler.
	/// The app writes full exception details (including stack traces) to a crash.log
	/// file in its cache directory. On Android, falls back to logcat if the crash log
	/// file is unavailable. Returns null if no crash log exists.
	/// </summary>
	public string? CaptureDeviceCrashLog() => Config.ReadCrashLog(GetDeviceId());

	/// <summary>
	/// Clears the device's app log buffer so subsequent captures only contain entries from
	/// the current app launch. Maps to <c>adb logcat -c</c> on Android; no-op on iOS.
	/// </summary>
	public void ClearAppLogs() => Config.ClearAppLogs(GetDeviceId());

	/// <summary>
	/// Queries the device's status bar height via the Appium driver.
	/// </summary>
	private Rectangle GetStatusBarMask() => new(0, 0, App.Manage().Window.Size.Width, Config.GetStatusBarHeight(App));

	/// <summary>
	/// Queries the device's home indicator / navigation bar height via the Appium driver.
	/// </summary>
	private Rectangle GetHomeIndicatorMask()
	{
		var windowSize = App.Manage().Window.Size;
		var height = Config.GetHomeIndicatorHeight(App);
		return new(0, windowSize.Height - height, windowSize.Width, height);
	}

	/// <summary>
	/// Sends a simulated push notification to an iOS simulator via <c>xcrun simctl push</c>.
	/// </summary>
	/// <param name="payloadFilePath">Absolute path to the APNS JSON payload file.</param>
	public void SendIosSimulatorPush(string payloadFilePath)
	{
		var result = ProcessRunner.RunWithResult(
			"xcrun",
			["simctl", "push", GetDeviceId(), Config.AppIdentifier, payloadFilePath]
		);
		if (result is null || result.ExitCode != 0)
		{
			var detail = result?.Error.Trim() ?? "no result";
			throw new InvalidOperationException(
				$"`xcrun simctl push` failed for '{payloadFilePath}' (exit {result?.ExitCode ?? -1}): {detail}"
			);
		}
	}

	/// <summary>
	/// Taps the notification banner at the top of the screen. The banner must be visible
	/// (e.g., on the home screen after a push notification). Uses W3C Actions to tap the
	/// middle of where notification banners appear on this device.
	/// </summary>
	public void TapNotificationBanner()
	{
		var size = App.Manage().Window.Size;
		var finger = new PointerInputDevice(PointerKind.Touch, "finger");
		var bannerCenter = (Config.NotificationBannerTop + Config.NotificationBannerBottom) / 2.0;
		if (_screenScale == 0)
		{
			using var screenshot = App.GetScreenshot().AsImage();
			RecordScreenScale(screenshot.Height, App.Manage().Window.Size.Height);
		}

		var sequence = new ActionSequence(finger, 0)
			.AddAction(
				finger.CreatePointerMove(
					CoordinateOrigin.Viewport,
					size.Width / 2,
					(int)Math.Round(bannerCenter / _screenScale),
					TimeSpan.Zero
				)
			)
			.AddAction(finger.CreatePointerDown(MouseButton.Left))
			.AddAction(finger.CreatePointerUp(MouseButton.Left));
		App.PerformActions([sequence]);
	}

	/// <summary>
	/// Presses the home button on the device, backgrounding the app.
	/// </summary>
	public void PressHomeButton()
	{
		Config.PressHomeButton(App);
		WaitForAppToSettle(500);
	}
}
