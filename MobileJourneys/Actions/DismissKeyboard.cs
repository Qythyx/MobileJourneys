namespace MobileJourneys.Actions;

/// <summary>
/// Dismisses the on-screen keyboard. Uses driver.HideKeyboard() on both platforms.
/// </summary>
public sealed record DismissKeyboard() : JourneyAction
{
	/// <inheritdoc/>
	public override void Execute(TestDriver driver) => driver.DismissKeyboard();
}
