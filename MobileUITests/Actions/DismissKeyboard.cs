namespace MobileUITests.Actions;

/// <summary>
/// Dismisses the on-screen keyboard. Uses driver.HideKeyboard() on both platforms.
/// </summary>
public sealed record DismissKeyboard() : JourneyAction
{
	public override void Execute(TestDriver driver) => driver.DismissKeyboard();
}
