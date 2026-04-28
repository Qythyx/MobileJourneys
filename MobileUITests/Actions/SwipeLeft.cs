namespace MobileUITests.Actions;

/// <summary>
/// When OnElementId is provided, targets that scrollable element for more deterministic
/// behavior (recommended for carousels). Otherwise falls back to a generic screen gesture.
/// </summary>
/// <param name="OnElementId">The ID of the element to swipe.</param>
public sealed record SwipeLeft(string OnElementId) : JourneyAction(OnElementId)
{
	public override void Execute(TestDriver driver) => driver.SwipeLeft(OnElementId);
}
