namespace MobileJourneys.Actions;

/// <summary>
/// Switches the system theme to the opposite of the current test fixture's theme
/// (light → dark or dark → light).
/// </summary>
public sealed record InvertSystemTheme() : JourneyAction
{
	public override void Execute(TestDriver driver)
	{
		SimulatorHelper.SetSystemTheme(!driver.Config.IsLightTheme, driver.Config.Platform, driver.GetDeviceId());
		TestDriver.WaitForAppToSettle(500);
	}
}
