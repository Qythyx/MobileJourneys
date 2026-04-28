using OpenQA.Selenium.Appium;

namespace MobileJourneys;

/// <summary>
/// Launch / terminate / relaunch the app under test, with the journey's
/// <see cref="IJourneyEnvironment"/> environment variables passed in. iOS uses
/// <c>mobile: launchApp</c>; Android uses <c>am start-activity</c> via
/// <c>mobile: shell</c> so per-launch env vars can be supplied as <c>--es</c> extras.
/// </summary>
public static class AppLifecycle
{
	/// <summary>Launches the app on the connected device.</summary>
	/// <param name="driver">The Appium driver.</param>
	/// <param name="config">Platform fixture identifying the device and the app's bundle ID/package.</param>
	/// <param name="environment">Per-journey mock state to pass as env vars.</param>
	public static void LaunchApp(AppiumDriver driver, PlatformConfig config, IJourneyEnvironment environment)
	{
		var envDict = environment.GetEnvVars();

		switch (config)
		{
			case IosPlatformConfig ios:
				var iosArgs = new Dictionary<string, object>
				{
					["bundleId"] = ios.AppIdentifier,
					["environment"] = envDict,
				};

				_ = driver.ExecuteScript("mobile: launchApp", iosArgs);
				break;

			case AndroidPlatformConfig android:
				var amArgs = new List<string>
				{
					"start-activity",
					"-S",
					"-n",
					$"{android.AppIdentifier}/{android.ResolvedMainActivity}",
				};

				foreach (var kv in envDict)
				{
					amArgs.AddRange(["--es", kv.Key, $"'{kv.Value}'"]);
				}

				_ = driver.ExecuteScript(
					"mobile: shell",
					new Dictionary<string, object> { ["command"] = "am", ["args"] = amArgs }
				);
				break;
		}
	}

	/// <summary>Force-terminates the app on the connected device.</summary>
	/// <param name="driver">The Appium driver.</param>
	/// <param name="config">Platform fixture identifying the device and the app's bundle ID/package.</param>
	public static void TerminateApp(AppiumDriver driver, PlatformConfig config)
	{
		switch (config)
		{
			case IosPlatformConfig ios:
				_ = driver.ExecuteScript(
					"mobile: terminateApp",
					new Dictionary<string, object> { ["bundleId"] = ios.AppIdentifier }
				);
				break;

			case AndroidPlatformConfig android:
				_ = driver.ExecuteScript(
					"mobile: terminateApp",
					new Dictionary<string, object> { ["appId"] = android.AppIdentifier }
				);
				break;
		}
	}

	/// <summary>Terminates the app and relaunches it with fresh environment variables.</summary>
	/// <param name="driver">The Appium driver.</param>
	/// <param name="config">Platform fixture identifying the device and the app's bundle ID/package.</param>
	/// <param name="environment">Per-journey mock state to pass as env vars.</param>
	public static void RelaunchApp(AppiumDriver driver, PlatformConfig config, IJourneyEnvironment environment)
	{
		TerminateApp(driver, config);
		LaunchApp(driver, config, environment);
	}
}
