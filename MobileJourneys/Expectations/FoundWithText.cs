namespace MobileJourneys.Expectations;

/// <summary>
/// Waits for an element with the given AutomationId to be found and contain one of the
/// expected text values (for supporting multiple languages).
/// </summary>
/// <param name="AutomationId">The AutomationId of the element that must be found.</param>
/// <param name="ExpectedTexts">One or more text values the element may contain (any match succeeds).</param>
public sealed record FoundWithText(string AutomationId, params string[] ExpectedTexts) : Expectation(AutomationId)
{
	/// <inheritdoc/>
	public override void Verify(TestDriver driver) => driver.FindElementWithText(AutomationId, ExpectedTexts);
}
