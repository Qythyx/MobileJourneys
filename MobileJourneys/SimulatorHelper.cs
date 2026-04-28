using System.Diagnostics;
using System.Drawing;
using OpenQA.Selenium.Appium;

namespace MobileJourneys;

/// <summary>
/// Out-of-process helpers that drive the simulator/emulator via <c>xcrun simctl</c> (iOS)
/// or <c>adb</c> (Android). Used to seed system-wide state — theme, font size, hardware
/// keyboard — before each journey runs.
/// </summary>
public static class SimulatorHelper
{
	/// <summary>Sets the system appearance to light or dark mode on the given device.</summary>
	/// <param name="isLightTheme"><c>true</c> for light, <c>false</c> for dark.</param>
	/// <param name="platform">The mobile platform.</param>
	/// <param name="deviceId">Simulator UDID (iOS) or emulator transport ID (Android).</param>
	public static void SetSystemTheme(bool isLightTheme, TestPlatform platform, string deviceId)
	{
		if (platform == TestPlatform.iOS)
		{
			var iosAppearance = isLightTheme ? "light" : "dark";
			RunProcess("xcrun", $"simctl ui {deviceId} appearance {iosAppearance}");
		}
		else
		{
			var androidNightMode = isLightTheme ? "no" : "yes";
			RunProcess(AdbPath, $"-s {deviceId} shell cmd uimode night {androidNightMode}");
		}
	}

	/// <summary>Sets the system-wide font size to a category from <see cref="SystemFontSize"/>.</summary>
	/// <param name="size">The font size category.</param>
	/// <param name="platform">The mobile platform.</param>
	/// <param name="deviceId">Simulator UDID (iOS) or emulator transport ID (Android).</param>
	public static void SetSystemFontSize(SystemFontSize size, TestPlatform platform, string deviceId)
	{
		if (platform == TestPlatform.iOS)
		{
			RunProcess("xcrun", $"simctl ui {deviceId} content_size {ToIosContentSize(size)}");
		}
		else
		{
			var scale = ToAndroidFontScale(size).ToString(System.Globalization.CultureInfo.InvariantCulture);
			RunProcess(AdbPath, $"-s {deviceId} shell settings put system font_scale {scale}");
		}
	}

	private static string ToIosContentSize(SystemFontSize size) =>
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

	private static double ToAndroidFontScale(SystemFontSize size) =>
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

	/// <summary>
	/// Enables hardware keyboard on an iOS simulator so the software keyboard doesn't appear.
	/// The Appium <c>connectHardwareKeyboard</c> capability only works when Appium launches the
	/// simulator; for pre-booted simulators the preference must be set directly.
	/// </summary>
	/// <param name="deviceId">The ID of the device.</param>
	public static void EnableiOSHardwareKeyboard(string deviceId) =>
		RunProcess(
			"xcrun",
			$"simctl spawn {deviceId} defaults write com.apple.Preferences ConnectHardwareKeyboard -bool true"
		);

	private static string AdbPath => $"{Environment.GetEnvironmentVariable("ANDROID_HOME")}/platform-tools/adb";

	private static void RunProcess(string fileName, string arguments)
	{
		using var process = Process.Start(
			new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			}
		);

		_ = process?.WaitForExit(TimeSpan.FromSeconds(10));
	}

	/// <summary>Gets a nested dictionary value by key. Throws if the value isn't a dictionary.</summary>
	/// <param name="parent">The parent dictionary.</param>
	/// <param name="key">Key to look up.</param>
	public static Dictionary<string, object> GetDict(this Dictionary<string, object> parent, string key) =>
		(Dictionary<string, object>)parent[key];

	/// <summary>Gets a value by key and converts it to <see cref="int"/>.</summary>
	/// <param name="parent">The parent dictionary.</param>
	/// <param name="key">Key to look up.</param>
	public static int GetInt(this Dictionary<string, object> parent, string key) => GetInt(parent[key]);

	/// <summary>Reads <c>left</c>/<c>top</c>/<c>width</c>/<c>height</c> int keys into a <see cref="Rectangle"/>.</summary>
	/// <param name="parent">Dictionary with the four bounds keys.</param>
	public static Rectangle GetRectangle(this Dictionary<string, object> parent) =>
		new(parent.GetInt("left"), parent.GetInt("top"), parent.GetInt("width"), parent.GetInt("height"));

	/// <summary>Runs an Appium script that returns a dictionary, casting the result.</summary>
	/// <param name="app">Appium driver.</param>
	/// <param name="script">Script name (e.g., <c>"mobile: viewportRect"</c>).</param>
	public static Dictionary<string, object> GetDict(this AppiumDriver app, string script) =>
		(Dictionary<string, object>)app.ExecuteScript(script)!;

	private static int GetInt(object obj) => Convert.ToInt32(obj, System.Globalization.CultureInfo.InvariantCulture);
}
