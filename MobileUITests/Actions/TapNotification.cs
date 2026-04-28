namespace MobileUITests.Actions;

/// <summary>
/// Taps the notification banner at the top of the screen. The banner must be visible
/// (e.g., on the home/lock screen after a push notification was triggered).
/// </summary>
public sealed record TapNotification() : JourneyAction
{
	public override void Execute(TestDriver driver) => driver.TapNotificationBanner();
}
