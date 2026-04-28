namespace MobileJourneys.Actions;

/// <summary>
/// Swipes right on the element with the given AutomationId. Targets a specific scrollable
/// element for deterministic gestures (recommended for carousels and lists).
/// </summary>
/// <param name="AutomationId">The AutomationId of the element to swipe.</param>
public sealed record SwipeRight(string AutomationId) : JourneyAction(AutomationId)
{
	/// <inheritdoc/>
	public override void Execute(TestDriver driver) => driver.SwipeRight(AutomationId);
}
