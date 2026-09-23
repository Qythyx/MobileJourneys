namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for an element with the given AutomationId to be absent within the wait budget.
/// </summary>
/// <param name="AutomationId">The AutomationId of the element that must not be found.</param>
public sealed record NotFound(string AutomationId) : Expectation(AutomationId)
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) => driver.WaitForElementNotFound(AutomationId);
}
