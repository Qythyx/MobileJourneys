namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for a system alert/dialog to be found within the wait budget.
/// </summary>
public sealed record AlertFound() : Expectation
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) => driver.WaitForAlert();
}
