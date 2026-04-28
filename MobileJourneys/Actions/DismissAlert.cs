namespace MobileJourneys.Actions;

public sealed record DismissAlert() : JourneyAction
{
	public override void Execute(TestDriver driver) => driver.DismissAlertIfPresent(TimeSpan.FromSeconds(2));
}
