namespace MobileJourneys;

/// <summary>
/// A typed expectation to verify after executing a journey action. Expectations observe
/// state — they assert visibility, text content, alert presence — without mutating it.
/// Processed in array order; the sequence matters for readiness gates.
/// </summary>
/// <param name="Target">Optional identifier used to build the step label. When the target
/// contains underscores (e.g., "ActivityOverlay_Overlay"), only the last segment is used
/// (e.g., "Overlay").</param>
public abstract record Expectation(string? Target = null) : JourneyOp(Target)
{
	/// <summary>
	/// Verifies this expectation using the provided test driver.
	/// </summary>
	/// <param name="driver">The test driver providing access to the app and device.</param>
	public abstract void Verify(TestDriver driver);
}
