using OpenQA.Selenium.Appium;

namespace MobileUITests;

public static class AppLifecycle
{
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

	public static void RelaunchApp(AppiumDriver driver, PlatformConfig config, IJourneyEnvironment environment)
	{
		TerminateApp(driver, config);
		LaunchApp(driver, config, environment);
	}
}
