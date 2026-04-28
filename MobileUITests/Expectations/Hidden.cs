namespace MobileUITests.Expectations;

/// <summary>
/// Waits for an element with the given AutomationId to not be visible within the timeout.
/// </summary>
/// <param name="AutomationId">The AutomationId of the element that must be hidden.</param>
/// <param name="TimeoutSeconds">Maximum seconds to wait before failing.</param>
public sealed record Hidden(string AutomationId, int TimeoutSeconds = 10) : Expectation(AutomationId)
{
	public override void Verify(TestDriver driver) =>
		driver.WaitForElementGone(AutomationId, TimeSpan.FromSeconds(TimeoutSeconds));
}
