using OpenQA.Selenium.Appium.Service;
using OpenQA.Selenium.Appium.Service.Options;

namespace MobileJourneys;

/// <summary>
/// Manages the lifecycle of a local Appium server process. The framework starts one
/// before the test session and disposes it after. Single-process; idempotent start.
/// </summary>
public static class AppiumServerHelper
{
	private static AppiumLocalService? _appiumLocalService;

	/// <summary>The default loopback address Appium binds to.</summary>
	public const string DefaultHostAddress = "127.0.0.1";

	/// <summary>The default port Appium listens on.</summary>
	public const int DefaultHostPort = 4723;

	/// <summary>
	/// Starts a local Appium server if one isn't already running. Adds the
	/// <c>--allow-insecure adb_shell</c> arg required by the driver's <c>mobile: shell</c>
	/// helpers (used by <see cref="AppLifecycle.LaunchApp"/> on Android).
	/// </summary>
	/// <param name="host">Bind address; defaults to <see cref="DefaultHostAddress"/>.</param>
	/// <param name="port">Bind port; defaults to <see cref="DefaultHostPort"/>.</param>
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

	/// <summary>Disposes the local Appium server started by <see cref="StartAppiumLocalServer"/>.</summary>
	public static void DisposeAppiumLocalServer()
	{
		_appiumLocalService?.Dispose();
		_appiumLocalService = null;
	}
}
