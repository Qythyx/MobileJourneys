namespace MobileJourneys.Actions;

/// <summary>
/// Clears the target element and types the given text into it, then dismisses the keyboard.
/// </summary>
/// <remarks>
/// On iOS, sending text that contains spaces is unreliable — the XCUITest backend occasionally
/// drops or duplicates space characters. Avoid spaces in test inputs where possible (use
/// no-space substitutes like <c>"TestUser"</c> instead of <c>"Test User"</c>).
/// </remarks>
/// <param name="AutomationId">The AutomationId of the input element.</param>
/// <param name="Text">The text to type. Avoid spaces on iOS.</param>
public sealed record TypeText(string AutomationId, string Text) : JourneyAction(AutomationId)
{
	/// <inheritdoc/>
	public override void Execute(TestDriver driver)
	{
		var element = driver.FindElement(AutomationId, TimeSpan.FromSeconds(5));
		element.Clear();
		element.SendKeys(Text);
		driver.DismissKeyboard();
	}
}
