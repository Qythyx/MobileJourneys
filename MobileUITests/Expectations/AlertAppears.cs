namespace MobileUITests.Expectations;

/// <summary>
/// Waits for a system alert/dialog to appear within the given timeout.
/// </summary>
/// <param name="TimeoutSeconds">Maximum seconds to wait before failing.</param>
public sealed record AlertAppears(int TimeoutSeconds = 10) : Expectation
{
	public override void Verify(TestDriver driver) => driver.WaitForAlert(TimeSpan.FromSeconds(TimeoutSeconds));
}
