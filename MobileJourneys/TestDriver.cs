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
/// <param name="deepLinkScheme">URL scheme without "://" used by <see cref="ScrollToElement"/> and consumer-side notification actions.</param>
/// <param name="screenshotManager">Instance used for baseline capture and comparison.</param>
public sealed class TestDriver(
	AppiumDriver app,
	PlatformConfig config,
	string deepLinkScheme,
	ScreenshotManager screenshotManager
)
{
	private enum Phase
	{
		HomeStabilizing = 0,
		WaitingForChange = 1,
		BannerStabilizing = 2,
	}

	/// <summary>
	/// AutomationId the consumer's app should set on the content view immediately
	/// below the notification banner. Used by the framework to mask out the banner
	/// region when comparing screenshots.
	/// </summary>
	public const string AutomationIdBelowNotification = "ScreenBelowNotification";

	/// <summary>The underlying Appium driver.</summary>
	public AppiumDriver App { get; } = app;

	/// <summary>The platform fixture this driver is bound to.</summary>
	public PlatformConfig Config { get; } = config;

	/// <summary>URL scheme used to open in-app deep links (without the "://" suffix).</summary>
	public string DeepLinkScheme { get; } = deepLinkScheme;

	/// <summary>
	/// The current journey's environment. Populated by <c>JourneyRunner</c> before each
	/// journey runs; consumer-side actions read this to derive fixture-aware behavior
	/// (e.g., picking the right localized label).
	/// </summary>
	public IJourneyEnvironment CurrentJourneyEnv { get; set; } = null!;

	private Rectangle[] SystemMasks =>
		field ??= [
			.. new Rectangle[] { GetStatusBarMask(), GetHomeIndicatorMask() }.Where(rect =>
				rect.Width > 0 && rect.Height > 0
			),
		];

	/// <summary>
	/// When set, <see cref="DoActionAndCompareWithBaseline"/> uses these bytes instead of
	/// taking an Appium screenshot. Cleared after use. Used for device-level screenshots
	/// (e.g., native notification banners captured via <c>xcrun simctl io</c>).
	/// </summary>
	private byte[]? _screenshotPngBytes;

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
		Config.LaunchApp(App, environment);
	}

	/// <summary>Polls until the element with the given AutomationId is no longer present or the timeout elapses.</summary>
	public void WaitForElementGone(string automationId, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				_ = App.FindElement(MobileBy.Id(automationId));
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
			// Try resource-id first, then fall back to content-desc (accessibility ID).
			// On Android, MAUI maps AutomationId to resource-id for most elements, but
			// Shell.ItemTemplate bindings map to content-desc instead.
			return wait.Until(d =>
			{
				AppiumElement element;
				try
				{
					element = (AppiumElement)d.FindElement(MobileBy.Id(automationId));
				}
				catch (NoSuchElementException)
				{
					element = (AppiumElement)d.FindElement(MobileBy.AccessibilityId(automationId));
				}

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
	/// Polls screenshots until two consecutive captures are identical (skipping masked regions),
	/// then compares the stable screenshot against a baseline image.
	/// </summary>
	/// <param name="action">The test action to run.</param>
	/// <param name="journeyName">Subfolder within the screenshots directory.</param>
	/// <param name="stepName">Baseline filename (without extension).</param>
	/// <param name="maskElementIds">AutomationIds of elements to exclude from stability polling and baseline comparison.</param>
	/// <param name="prefetchMasks"><c>true</c> to query the mask element IDs before running the
	/// test action. This is useful if the action will cause the elements to become unavailable
	/// such as showing a alert above them.</param>
	/// <exception cref="TimeoutException">Thrown when the screen does not stabilize within a fixed amount of time.</exception>
	public ScreenshotComparisonResult DoActionAndCompareWithBaseline(
		Action action,
		string journeyName,
		string stepName,
		string[] maskElementIds,
		bool prefetchMasks
	)
	{
		if (!prefetchMasks)
		{
			action();
		}

		var windowSize = App.Manage().Window.Size;
		var previousImage = _screenshotPngBytes is not null
			? Image.Load<Rgb24>(_screenshotPngBytes)
			: App.GetScreenshot().AsImage();
		var maskRegions = ImageHelpers.ScaleMaskRegions(
			[.. maskElementIds.Select(id => GetElementRectangle(windowSize, id)).Concat(SystemMasks)],
			windowSize,
			new System.Drawing.Size(previousImage.Width, previousImage.Height)
		);

		if (prefetchMasks)
		{
			action();
		}

		// If a device-level screenshot was captured (e.g., native notification banner),
		// use it directly instead of polling Appium.
		if (_screenshotPngBytes is not null)
		{
			_screenshotPngBytes = null;
			return screenshotManager.CompareWithBaselineAndDispose(
				previousImage,
				new(Config, journeyName, stepName),
				maskRegions
			);
		}

		const int MaxWaitMs = 5000;
		const int MinMsBetweenScreenshots = 300;

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		var elapsed = stopwatch.Elapsed;
		while (stopwatch.Elapsed.TotalMilliseconds < MaxWaitMs)
		{
			var screenshot = App.GetScreenshot().AsImage();
			if ((stopwatch.Elapsed - elapsed).TotalMilliseconds < MinMsBetweenScreenshots)
			{
				screenshot.Dispose();
				Task.Delay(100).Wait();
				continue;
			}
			elapsed = stopwatch.Elapsed;
			if (ImageHelpers.AreImagesEqual(screenshot, previousImage, maskRegions))
			{
				previousImage.Dispose();
				return screenshotManager.CompareWithBaselineAndDispose(
					screenshot,
					new(Config, journeyName, stepName),
					maskRegions
				);
			}

			previousImage.Dispose();
			previousImage = screenshot;
		}

		previousImage.Dispose();
		throw new TimeoutException($"Screen did not stabilize within {MaxWaitMs}ms");
	}

	private Rectangle GetElementRectangle(System.Drawing.Size windowSize, string automationId)
	{
		var notificationHeight = Config.NotificationBannerMaskHeight;

		// extend 2 pixel to account for rounding during resizing
		static Rectangle GetBounds(AppiumElement ele) =>
			new(ele.Location.X - 2, ele.Location.Y - 2, ele.Size.Width + 4, ele.Size.Height + 4);

		return automationId switch
		{
			AutomationIdBelowNotification => new(
				0,
				notificationHeight,
				windowSize.Width,
				windowSize.Height - notificationHeight
			),
			_ => GetBounds(App.FindElement(MobileBy.Id(automationId))),
		};
	}

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

	/// <summary>
	/// Scrolls to an element using a deep link to the app's <c>ScrollView.ScrollToAsync</c>,
	/// which is pixel-perfect since the framework controls the scroll position (unlike
	/// touch-based Appium scrolling which has inherent imprecision).
	/// </summary>
	/// <param name="automationId">The AutomationId of the element to scroll to.</param>
	public void ScrollToElement(string automationId)
	{
		DismissKeyboard();
		OpenDeepLink($"{DeepLinkScheme}://open/scroll?id={automationId}");
		WaitForAppToSettle(500);
	}

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
	/// Waits for a notification banner to appear on the device screen by polling device-level
	/// screenshots. First waits for the home screen to stabilize (two identical captures),
	/// then waits for the screen to change (notification arrived). Ignores changes that
	/// coincide with a clock minute rollover to avoid false positives from the time display.
	/// Stores the final screenshot in <see cref="_screenshotPngBytes"/> for baseline comparison.
	/// </summary>
	/// <param name="timeout">Maximum time to wait.</param>
	/// <exception cref="TimeoutException">Thrown when the notification banner does not appear within the timeout.</exception>
	public void WaitForNotificationBanner(TimeSpan timeout)
	{
		const int BannerHeight = 300;
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		var previousImage = CropToTop(CaptureDeviceScreenshotBytes(), BannerHeight);
		var previousMinute = DateTime.Now.Minute;
		// Phase tracking: HomeStabilizing -> WaitingForChange -> BannerStabilizing
		var phase = Phase.HomeStabilizing;

		while (stopwatch.Elapsed < timeout)
		{
			var fullBytes = CaptureDeviceScreenshotBytes();
			var currentImage = CropToTop(fullBytes, BannerHeight);
			var currentMinute = DateTime.Now.Minute;
			var identical = ImageHelpers.AreImagesEqual(currentImage, previousImage, []);

			switch (phase)
			{
				case Phase.HomeStabilizing:
					// Phase 1: wait for the home screen to stabilize (two identical frames).
					if (identical)
					{
						phase = Phase.WaitingForChange;
						currentImage.Dispose();
					}
					else
					{
						previousImage.Dispose();
						previousImage = currentImage;
					}

					previousMinute = currentMinute;
					continue;

				case Phase.WaitingForChange:
					// Phase 2: wait for the screen to change (notification appearing).
					if (!identical && currentMinute == previousMinute)
					{
						phase = Phase.BannerStabilizing;
						previousImage.Dispose();
						previousImage = currentImage;
						previousMinute = currentMinute;
						continue;
					}

					// Clock minute rolled over or still identical — update baseline.
					previousImage.Dispose();
					previousImage = currentImage;
					previousMinute = currentMinute;
					continue;

				case Phase.BannerStabilizing:
					// Phase 3: wait for the notification banner to finish animating
					// (two identical frames after the initial change).
					if (identical)
					{
						previousImage.Dispose();
						currentImage.Dispose();
						_screenshotPngBytes = fullBytes;
						return;
					}

					// Still animating — update baseline.
					previousImage.Dispose();
					previousImage = currentImage;
					previousMinute = currentMinute;
					continue;
			}
		}

		previousImage.Dispose();
		throw new TimeoutException($"Notification banner did not appear within {timeout.TotalSeconds}s");
	}

	/// <summary>
	/// Crops a full-screen PNG to the top <paramref name="height"/> pixels and returns it.
	/// </summary>
	/// <param name="pngBytes">Raw PNG bytes of the full screenshot.</param>
	/// <param name="height">Number of pixels to keep from the top.</param>
	private static Image<Rgb24> CropToTop(byte[] pngBytes, int height)
	{
		var image = Image.Load<Rgb24>(pngBytes);
		image.Mutate(x => x.Crop(image.Width, Math.Min(height, image.Height)));
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
	/// (e.g., on the home screen after a push notification). Uses W3C Actions to tap at
	/// the center-top of the viewport where notification banners appear on both platforms.
	/// </summary>
	public void TapNotificationBanner()
	{
		var size = App.Manage().Window.Size;
		var finger = new PointerInputDevice(PointerKind.Touch, "finger");
		var yOffset = Config.NotificationBannerTapYOffset;
		var sequence = new ActionSequence(finger, 0)
			.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, size.Width / 2, yOffset, TimeSpan.Zero))
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
