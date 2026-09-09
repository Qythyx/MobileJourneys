namespace MobileJourneys.Actions;

/// <summary>
/// Focuses the target element, clears it and types the given text into it, then dismisses the
/// keyboard. Focus moves the way it does under a customer's finger, so an input left for the next
/// one sees its unfocus; setting text through the accessibility tree alone moves nothing.
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
		element.Click();
		element.Clear();
		element.SendKeys(Text);
		driver.DismissKeyboard();
	}
}
