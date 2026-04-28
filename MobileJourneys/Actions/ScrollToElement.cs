namespace MobileJourneys.Actions;

/// <summary>
/// Scrolls the nearest scrollable container until the target element is visible. Uses
/// UiScrollable.scrollIntoView on Android and mobile:scroll on iOS.
/// </summary>
/// <param name="AutomationId">The ID of the element to scroll to.</param>
public sealed record ScrollToElement(string AutomationId) : JourneyAction(AutomationId)
{
	/// <inheritdoc/>
	public override void Execute(TestDriver driver) => driver.ScrollToElement(AutomationId);
}
