namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for a notification banner to appear on the device screen. First polls device
/// screenshots until the home screen stabilizes, then polls until the screen changes
/// (indicating the notification arrived). Ignores changes that coincide with a clock
/// minute rollover to avoid false positives.
/// </summary>
public sealed record WaitForNotification() : Expectation
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) => driver.WaitForNotificationBanner();
}
