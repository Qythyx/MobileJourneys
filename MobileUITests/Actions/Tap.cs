using OpenQA.Selenium;

namespace MobileUITests.Actions;

/// <summary>
/// Taps an element by AutomationId. On Android, retries up to 3 times if the element
/// becomes stale between find and click — this happens when the action follows an alert
/// dismissal or other re-render-prone event.
/// </summary>
/// <param name="AutomationId">The element to tap.</param>
public sealed record Tap(string AutomationId) : JourneyAction(AutomationId)
{
	public override void Execute(TestDriver driver)
	{
		for (var attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				driver.FindElement(AutomationId, TimeSpan.FromSeconds(5)).Click();
				return;
			}
			catch (StaleElementReferenceException) when (attempt < 2)
			{
				TestDriver.WaitForAppToSettle(300);
			}
		}
	}
}
