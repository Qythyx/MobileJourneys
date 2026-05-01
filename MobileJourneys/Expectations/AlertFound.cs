namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for a system alert/dialog to be found within the given timeout.
/// </summary>
/// <param name="TimeoutSeconds">Maximum seconds to wait before failing.</param>
public sealed record AlertFound(int TimeoutSeconds = 10) : Expectation
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) => driver.WaitForAlert(TimeSpan.FromSeconds(TimeoutSeconds));
}
