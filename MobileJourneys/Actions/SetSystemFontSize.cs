namespace MobileJourneys.Actions;

/// <summary>
/// Changes the device's system-wide font size to a category from <see cref="SystemFontSize"/>.
/// On iOS this maps to a content-size category; on Android to a numeric <c>font_scale</c>.
/// </summary>
/// <param name="Size">The system font size category to apply.</param>
public sealed record SetSystemFontSize(SystemFontSize Size) : JourneyAction(Size.ToString())
{
	/// <inheritdoc/>
	public override void Execute(TestDriver driver)
	{
		driver.Config.SetSystemFontSize(driver.GetDeviceId(), Size);
		TestDriver.WaitForAppToSettle(500);
	}
}
