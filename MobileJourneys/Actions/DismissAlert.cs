namespace MobileJourneys.Actions;

/// <summary>
/// Dismisses any visible system alert/dialog using Selenium's abstract Dismiss verb.
/// No-op when no alert is visible. For two-button MAUI alerts where the abstract verb
/// hits the wrong button, prefer <see cref="TapAlertButton"/> with the exact label.
/// </summary>
public sealed record DismissAlert() : JourneyAction
{
	/// <inheritdoc/>
	public override void Execute(TestDriver driver) => driver.DismissAlertIfPresent(TimeSpan.FromSeconds(2));
}
