namespace MobileUITests.Actions;

public sealed record TypeText(string AutomationId, string Text) : JourneyAction(AutomationId)
{
	public override void Execute(TestDriver driver)
	{
		var element = driver.FindElement(AutomationId, TimeSpan.FromSeconds(5));
		element.Clear();
		element.SendKeys(Text);
		driver.DismissKeyboard();
	}
}
