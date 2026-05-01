namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for an element with the given AutomationId to be not found within the timeout.
/// </summary>
/// <param name="AutomationId">The AutomationId of the element that must not be found.</param>
/// <param name="TimeoutSeconds">Maximum seconds to wait before failing.</param>
public sealed record NotFound(string AutomationId, int TimeoutSeconds = 10) : Expectation(AutomationId)
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) =>
		driver.WaitForElementNotFound(AutomationId, TimeSpan.FromSeconds(TimeoutSeconds));
}
