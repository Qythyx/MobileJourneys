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

public sealed class TestDriver(AppiumDriver app, PlatformConfig config, string deepLinkScheme)
{
	private enum Phase
	{
		HomeStabilizing = 0,
		WaitingForChange = 1,
		BannerStabilizing = 2,
	}

	public const string AutomationIdBelowNotification = "ScreenBelowNotification";
	public AppiumDriver App { get; } = app;
	public PlatformConfig Config { get; } = config;
	public string DeepLinkScheme { get; } = deepLinkScheme;
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

	public string GetDeviceId()
	{
		var capabilityName = Config.Platform == TestPlatform.iOS ? "udid" : "deviceUDID";
		return App.Capabilities.GetCapability(capabilityName)?.ToString()
			?? throw new InvalidOperationException(
				$"Could not get device ID from driver capability '{capabilityName}'"
			);
	}

	public void WaitForElementGone(string automationId, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				_ = App.FindElement(MobileBy.Id(automationId));
				Task.Delay(250).Wait();
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
	/// Thrown when a <paramref name="condition"/> is supplied and its predicate never returns true
	/// before the timeout; the exception message is built from <c>condition.message</c>.
	/// </exception>
	/// <exception cref="WebDriverTimeoutException">
	/// Thrown when no <paramref name="condition"/> is supplied and the element cannot be found
	/// before the timeout.
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
	/// Thrown when the element is found but does not contain any expected text within the timeout.
	/// </exception>
	/// <exception cref="WebDriverTimeoutException">
	/// Thrown when the element is never found within the timeout.
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
			: App.GetScreenshot().AsImage(false);
		var maskRegions = ScreenshotHelper.ScaleMaskRegions(
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
			return ScreenshotHelper.CompareWithBaselineAndDispose(
				previousImage,
				Config,
				journeyName,
				stepName,
				maskRegions
			);
		}

		const int MaxWaitMs = 5000;
		const int MinMsBetweenScreenshots = 300;

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		var elapsed = stopwatch.Elapsed;
		while (stopwatch.Elapsed.TotalMilliseconds < MaxWaitMs)
		{
			var screenshot = App.GetScreenshot().AsImage(false);
			if ((stopwatch.Elapsed - elapsed).TotalMilliseconds < MinMsBetweenScreenshots)
			{
				screenshot.Dispose();
				Task.Delay(100).Wait();
				continue;
			}
			elapsed = stopwatch.Elapsed;
			if (ScreenshotHelper.AreImagesStable(screenshot, previousImage, maskRegions))
			{
				previousImage.Dispose();
				return ScreenshotHelper.CompareWithBaselineAndDispose(
					screenshot,
					Config,
					journeyName,
					stepName,
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
		// approximate height
		var notificationHeight = Config.Platform == TestPlatform.Android ? 350 : 110;

		// extend 1 pixel to account for rounding during resizing
		static Rectangle GetBounds(AppiumElement ele) =>
			new(ele.Location.X - 1, ele.Location.Y - 1, ele.Size.Width + 2, ele.Size.Height + 2);

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

	public void SwipeLeft(string onElementId) => SwipeOnElement(onElementId, "left");

	public void SwipeRight(string onElementId) => SwipeOnElement(onElementId, "right");

	public void DismissKeyboard()
	{
		try
		{
			App.HideKeyboard();
		}
		catch
		{
			/* HideKeyboard may not be supported */
		}

		// HideKeyboard() is unreliable on iOS 26+ — it may silently fail
		// or throw. Try the MAUI Done button (input accessory toolbar added by
		// MauiDoneAccessoryView for Editor/Picker controls) first, then fall
		// back to the keyboard Return key for Entry controls.
		if (Config.Platform == TestPlatform.iOS)
		{
			// Try the MAUI Done button in the input accessory toolbar (added by
			// MauiDoneAccessoryView for Editor/Picker controls). Match by structure
			// rather than name since the button label is localized.
			try
			{
				App.FindElement(By.XPath("//XCUIElementTypeToolbar//XCUIElementTypeButton")).Click();
				return;
			}
			catch
			{
				/* no toolbar button — not an Editor/Picker, or keyboard not visible */
			}

			try
			{
				App.FindElement(By.XPath("//XCUIElementTypeKeyboard//XCUIElementTypeButton[@name='Return']")).Click();
			}
			catch
			{
				/* keyboard may not be visible */
			}
		}
	}

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

	public void OpenDeepLink(string url)
	{
		if (Config.Platform == TestPlatform.Android)
		{
			_ = App.ExecuteScript(
				"mobile: deepLink",
				new Dictionary<string, object> { ["url"] = url, ["package"] = Config.AppIdentifier }
			);
		}
		else
		{
			// iOS has no mobile: deepLink command. Navigate to the URL directly — XCUITest
			// delivers custom URL schemes to the foreground app without a system confirmation dialog.
			App.Navigate().GoToUrl(url);
		}
	}

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

			var alert = App.SwitchTo().Alert();
			if (Config.Platform == TestPlatform.iOS)
			{
				// On iOS XCUITest with MAUI, Accept() maps to the cancel button
				// of a two-button DisplayAlert (the one that does nothing).
				alert.Accept();
			}
			else
			{
				alert.Dismiss();
			}

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
		if (Config.Platform == TestPlatform.iOS)
		{
			App.FindElement(By.Name(buttonLabel)).Click();
		}
		else
		{
			// Android AlertDialog buttons don't have AccessibilityId. Find by text content.
			// MaterialAlertDialog renders button text in uppercase via textAllCaps, so the
			// @text attribute contains the uppercased form. Use translate() for a
			// case-insensitive match so journey definitions can use the natural-case label.
			var lower = buttonLabel.ToLowerInvariant();
			App.FindElement(
					By.XPath(
						$"//android.widget.Button[translate(@text,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='{lower}']"
					)
				)
				.Click();
		}

		WaitForAppToSettle(300);
	}

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
			var identical = ScreenshotHelper.AreImagesStable(currentImage, previousImage, []);

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
			if (Config.Platform == TestPlatform.iOS)
			{
				_ = RunShellCommand("xcrun", $"simctl io {GetDeviceId()} screenshot {tmpPath}");
			}
			else
			{
				const string DeviceTmp = "/data/local/tmp/test_screenshot.png";
				_ = RunAdbCommand($"shell screencap -p {DeviceTmp}");
				_ = RunAdbCommand($"pull {DeviceTmp} {tmpPath}");
				_ = RunAdbCommand($"shell rm {DeviceTmp}");
			}

			return File.ReadAllBytes(tmpPath);
		}
		finally
		{
			File.Delete(tmpPath);
		}
	}

	public void SelectPickerItem(string automationId, int itemIndex)
	{
		if (Config.Platform == TestPlatform.iOS)
		{
			// MAUI Picker on iOS can't be opened via any Appium XCUITest method — element.Click(),
			// mobile:tap, mobile:tapWithNumberOfTaps, mobile:touchAndHold, and W3C Actions API all
			// fail to trigger the UITapGestureRecognizer on the Picker's UITextField
			// (https://github.com/dotnet/maui/issues/28024). Use platform-specific workarounds
			// (e.g., MOCK_THEME env var) instead of this method for iOS picker interaction.
			throw new NotSupportedException(
				"MAUI Picker cannot be opened via Appium on iOS (dotnet/maui#28024). "
					+ "Use a platform-specific workaround instead."
			);
		}

		SelectPickerItemAndroid(automationId, itemIndex);
	}

	private void SelectPickerItemAndroid(string automationId, int itemIndex)
	{
		FindElement(automationId, TimeSpan.FromSeconds(5)).Click();
		WaitForAppToSettle(500);

		// Android MAUI Picker: opens an AlertDialog with CheckedTextView radio button items.
		// Wait for the dialog to appear before looking for items.
		var wait = new WebDriverWait(App, TimeSpan.FromSeconds(5));
		wait.IgnoreExceptionTypes(typeof(NoSuchElementException));
		_ = wait.Until(d => d.FindElements(MobileBy.ClassName("android.widget.CheckedTextView")).Count > 0);

		var items = App.FindElements(MobileBy.ClassName("android.widget.CheckedTextView"));
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

	/// <summary>
	/// Checks whether the app process has exited (actual crash) using Appium's queryAppState.
	/// Returns true if the app is not running; false if it's still alive.
	/// </summary>
	public bool IsAppCrashed()
	{
		try
		{
			var paramName = Config.Platform == TestPlatform.iOS ? "bundleId" : "appId";
			var result = App.ExecuteScript(
				"mobile: queryAppState",
				new Dictionary<string, object> { [paramName] = Config.AppIdentifier }
			);

			// queryAppState returns: 0 = not installed, 1 = not running, 3 = background, 4 = foreground
			return result is long state && state <= 1;
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
	/// <param name="appCrashed">True if the app crashed; otherwise false.</param>
	public string? CaptureDeviceCrashLog(bool appCrashed = false) =>
		Config.Platform == TestPlatform.Android ? ReadAndroidCrashLog(appCrashed) : ReadIosCrashLog();

	private string? ReadAndroidCrashLog(bool appCrashed)
	{
		// The app's private data dir (/data/data/<package>/) is only accessible via run-as.
		// Must specify -s <device> because multiple emulators may be running.
		const string crashLog = "crash.log";
		var package = Config.AppIdentifier;

		// When the app crashed, it may still be writing the log file. Retry a few times.
		var maxAttempts = appCrashed ? 3 : 1;
		for (var attempt = 0; attempt < maxAttempts; attempt++)
		{
			if (attempt > 0)
			{
				Task.Delay(500).Wait();
			}

			var result = RunAdbCommandFull($"shell run-as {package} cat cache/{crashLog}");
			if (result is null)
			{
				continue;
			}

			if (
				!string.IsNullOrWhiteSpace(result.Output)
				&& !result.Output.Contains("No such file", StringComparison.Ordinal)
			)
			{
				_ = RunAdbCommand($"shell run-as {package} rm cache/{crashLog}");
				return result.Output.Trim();
			}
		}

		// crash.log not available — fall back to logcat for the crash stack trace.
		var logcat = CaptureAndroidLogcat();
		return logcat is not null ? $"[from logcat — crash.log was not available]\n{logcat}" : null;
	}

	/// <summary>
	/// Clears the Android logcat buffer so that subsequent captures only contain
	/// entries from the current app launch. No-op on iOS.
	/// </summary>
	public void ClearAndroidLogcat()
	{
		if (Config.Platform == TestPlatform.Android)
		{
			_ = RunAdbCommand("logcat -c");
		}
	}

	/// <summary>
	/// Captures recent crash-related output from Android logcat as a fallback when
	/// the app's crash.log file is not available.
	/// </summary>
	private string? CaptureAndroidLogcat()
	{
		// AndroidRuntime:E  — fatal Java/Kotlin exception stack traces
		// MonoUnhandled:E   — .NET unhandled exception output
		// DOTNET:E          — .NET runtime error output
		// monodroid:F       — native startup failures (e.g. Fast Deployment missing assemblies)
		// DEBUG:F           — native crash abort messages with stack traces
		// Logcat is cleared before each app launch, so -t is not needed — all entries
		// in the buffer are from the current launch attempt.
		var result = RunAdbCommand("logcat -d AndroidRuntime:E MonoUnhandled:E DOTNET:E monodroid:F DEBUG:F *:S");

		return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
	}

	private string? RunAdbCommand(string arguments)
	{
		var adb = $"{Environment.GetEnvironmentVariable("ANDROID_HOME")}/platform-tools/adb";
		return RunShellCommand(adb, $"-s {GetDeviceId()} {arguments}")?.Output;
	}

	private ShellResult? RunAdbCommandFull(string arguments)
	{
		var adb = $"{Environment.GetEnvironmentVariable("ANDROID_HOME")}/platform-tools/adb";
		return RunShellCommand(adb, $"-s {GetDeviceId()} {arguments}");
	}

	private string? ReadIosCrashLog()
	{
		// Get the app's data container path on the iOS simulator
		var deviceId = GetDeviceId();
		var containerPath = RunShellCommand("xcrun", $"simctl get_app_container {deviceId} {Config.AppIdentifier} data")
			?.Output?.Trim();

		if (string.IsNullOrWhiteSpace(containerPath))
		{
			return null;
		}

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

	private sealed record ShellResult(string Output, string Error, int ExitCode);

	private static ShellResult? RunShellCommand(string command, string arguments)
	{
		try
		{
			using var process = new System.Diagnostics.Process();
			process.StartInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = command,
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			if (process.Start())
			{
				var output = process.StandardOutput.ReadToEnd();
				var error = process.StandardError.ReadToEnd();
				_ = process.WaitForExit(5000);
				return new ShellResult(output, error, process.ExitCode);
			}

			return new ShellResult("", $"Process '{command} {arguments}' failed to start", -1);
		}
		catch (Exception ex)
		{
			return new ShellResult("", $"Exception running '{command} {arguments}': {ex.Message}", -1);
		}
	}

	/// <summary>
	/// Queries the device's status bar height via the Appium driver.
	/// </summary>
	private Rectangle GetStatusBarMask()
	{
		var height = Config.Platform switch
		{
			TestPlatform.iOS => App.GetDict("mobile: deviceScreenInfo").GetDict("statusBarSize").GetInt("height"),
			TestPlatform.Android => App.GetDict("mobile: getSystemBars").GetDict("statusBar").GetInt("height"),
			_ => 0,
		};
		return new(0, 0, App.Manage().Window.Size.Width, height);
	}

	/// <summary>
	/// Queries the device's home indicator / navigation bar height via the Appium driver.
	/// </summary>
	private Rectangle GetHomeIndicatorMask()
	{
		var windowSize = App.Manage().Window.Size;
		var height = Config.Platform switch
		{
			TestPlatform.iOS => 34, // Can't probe it from Appium
			TestPlatform.Android => App.GetDict("mobile: getSystemBars").GetDict("navigationBar").GetInt("height"),
			_ => 0,
		};
		return new(0, windowSize.Height - height, windowSize.Width, height);
	}

	/// <summary>
	/// Sends a simulated push notification to an iOS simulator via <c>xcrun simctl push</c>.
	/// </summary>
	/// <param name="payloadFilePath">Absolute path to the APNS JSON payload file.</param>
	public void SendIosSimulatorPush(string payloadFilePath) =>
		_ = RunShellCommand("xcrun", $"simctl push {GetDeviceId()} {Config.AppIdentifier} {payloadFilePath}");

	/// <summary>
	/// Taps the notification banner at the top of the screen. The banner must be visible
	/// (e.g., on the home screen after a push notification). Uses W3C Actions to tap at
	/// the center-top of the viewport where notification banners appear on both platforms.
	/// </summary>
	public void TapNotificationBanner()
	{
		var size = App.Manage().Window.Size;
		var finger = new PointerInputDevice(PointerKind.Touch, "finger");
		var yOffset = Config.Platform == TestPlatform.Android ? 200 : 100;
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
		_ =
			Config.Platform == TestPlatform.iOS
				? App.ExecuteScript("mobile: pressButton", new Dictionary<string, object> { ["name"] = "home" })
				: App.ExecuteScript("mobile: pressKey", new Dictionary<string, object> { ["keycode"] = 3 });
		WaitForAppToSettle(500);
	}
}
