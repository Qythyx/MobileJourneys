using OpenQA.Selenium.Appium.Service;
using OpenQA.Selenium.Appium.Service.Options;

namespace MobileUITests;

public static class AppiumServerHelper
{
	private static AppiumLocalService? _appiumLocalService;

	public const string DefaultHostAddress = "127.0.0.1";
	public const int DefaultHostPort = 4723;

	public static void StartAppiumLocalServer(string host = DefaultHostAddress, int port = DefaultHostPort)
	{
		if (_appiumLocalService is not null)
		{
			return;
		}

		var args = new OptionCollector().AddArguments(new("--allow-insecure", "*:adb_shell"));
		var builder = new AppiumServiceBuilder().WithIPAddress(host).UsingPort(port).WithArguments(args);

		_appiumLocalService = builder.Build();
		_appiumLocalService.Start();
	}

	public static void DisposeAppiumLocalServer()
	{
		_appiumLocalService?.Dispose();
		_appiumLocalService = null;
	}
}
