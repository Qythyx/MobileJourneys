using System.Diagnostics;
using System.Drawing;
using OpenQA.Selenium.Appium;

namespace MobileUITests;

public static class SimulatorHelper
{
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

	public static void SetSystemFontSize(
		string iosContentSize,
		string androidFontScale,
		TestPlatform platform,
		string deviceId
	)
	{
		if (platform == TestPlatform.iOS)
		{
			RunProcess("xcrun", $"simctl ui {deviceId} content_size {iosContentSize}");
		}
		else
		{
			RunProcess(AdbPath, $"-s {deviceId} shell settings put system font_scale {androidFontScale}");
		}
	}

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

	public static Dictionary<string, object> GetDict(this Dictionary<string, object> parent, string key) =>
		(Dictionary<string, object>)parent[key];

	public static int GetInt(this Dictionary<string, object> parent, string key) => GetInt(parent[key]);

	public static Rectangle GetRectangle(this Dictionary<string, object> parent) =>
		new(parent.GetInt("left"), parent.GetInt("top"), parent.GetInt("width"), parent.GetInt("height"));

	public static Dictionary<string, object> GetDict(this AppiumDriver app, string script) =>
		(Dictionary<string, object>)app.ExecuteScript(script)!;

	private static int GetInt(object obj) => Convert.ToInt32(obj, System.Globalization.CultureInfo.InvariantCulture);
}
