namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for an element with the given AutomationId to be visible within the timeout.
/// </summary>
/// <param name="AutomationId">The AutomationId of the element that must be visible.</param>
/// <param name="TimeoutSeconds">Maximum seconds to wait before failing.</param>
public sealed record Visible(string AutomationId, int TimeoutSeconds = 10) : Expectation(AutomationId)
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) =>
		driver.FindElement(AutomationId, TimeSpan.FromSeconds(TimeoutSeconds));
}
