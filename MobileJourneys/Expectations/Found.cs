namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for an element with the given AutomationId to be found within the timeout.
/// </summary>
/// <param name="AutomationId">The AutomationId of the element that must be found.</param>
/// <param name="TimeoutSeconds">Maximum seconds to wait before failing.</param>
public sealed record Found(string AutomationId, int TimeoutSeconds = 10) : Expectation(AutomationId)
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) =>
		driver.FindElement(AutomationId, TimeSpan.FromSeconds(TimeoutSeconds));
}
