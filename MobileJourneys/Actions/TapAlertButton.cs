namespace MobileJourneys.Actions;

/// <summary>
/// Taps a specific button in a system alert/dialog by its label text. Unlike
/// <see cref="DismissAlert"/> which uses Selenium's abstract Accept/Dismiss (unreliable
/// for two-button MAUI alerts), this finds and clicks the exact button element.
/// </summary>
/// <param name="Label">The exact button text to tap.</param>
public sealed record TapAlertButton(string Label) : JourneyAction(Label)
{
	public override void Execute(TestDriver driver) => driver.TapAlertButton(Label);
}
