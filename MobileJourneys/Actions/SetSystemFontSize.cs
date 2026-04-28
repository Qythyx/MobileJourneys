namespace MobileJourneys.Actions;

/// <summary>
/// Changes the system font size. Accepts platform-specific values because iOS uses named
/// content size categories while Android uses a numeric font scale.
/// </summary>
/// <param name="IOSContentSize">iOS content size category (e.g., "extra-extra-large").</param>
/// <param name="AndroidFontScale">Android font scale float as string (e.g., "1.3").</param>
public sealed record SetSystemFontSize(string IOSContentSize, string AndroidFontScale) : JourneyAction(IOSContentSize)
{
	public override void Execute(TestDriver driver)
	{
		SimulatorHelper.SetSystemFontSize(
			IOSContentSize,
			AndroidFontScale,
			driver.Config.Platform,
			driver.GetDeviceId()
		);
		TestDriver.WaitForAppToSettle(500);
	}
}
