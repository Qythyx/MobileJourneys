namespace MobileJourneys.Actions;

/// <summary>
/// Switches the system theme to the opposite of the current test fixture's theme
/// (light → dark or dark → light).
/// </summary>
public sealed record InvertSystemTheme() : JourneyAction
{
	/// <inheritdoc/>
	public override void Execute(TestDriver driver)
	{
		driver.Config.SetSystemTheme(driver.GetDeviceId(), !driver.Config.IsLightTheme);
		TestDriver.WaitForAppToSettle(500);
	}
}
