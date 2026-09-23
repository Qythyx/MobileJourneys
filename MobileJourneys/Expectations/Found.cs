namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for an element with the given AutomationId to be found within the wait budget.
/// </summary>
/// <param name="AutomationId">The AutomationId of the element that must be found.</param>
public sealed record Found(string AutomationId) : Expectation(AutomationId)
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) => driver.FindElement(AutomationId);
}
