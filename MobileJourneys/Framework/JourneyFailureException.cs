namespace MobileJourneys.Framework;

/// <summary>
/// Thrown by <see cref="JourneyRunner"/> when a step fails (screenshot mismatch, app
/// crash, or any underlying driver/Appium exception). Carries enough context that the
/// test reporter or downstream tooling can identify which step of which journey failed.
/// The triggering exception (e.g., a Selenium <c>NoSuchElementException</c>) is preserved
/// as <see cref="Exception.InnerException"/>.
/// </summary>
internal sealed class JourneyFailureException(
	string message,
	JourneyDefinition journey,
	JourneyStep? step,
	int stepNumber,
	int totalSteps,
	string stepName,
	Exception? innerException = null
) : Exception(message, innerException)
{
	/// <summary>The journey that was running when the failure occurred.</summary>
	public JourneyDefinition Journey { get; } = journey;

	/// <summary>The step that was running, or <c>null</c> for the initial-expectations step.</summary>
	public JourneyStep? Step { get; } = step;

	/// <summary>1-based step index. Step 1 is the initial-expectations check.</summary>
	public int StepNumber { get; } = stepNumber;

	/// <summary>Total number of steps in the journey (initial expectations + N steps).</summary>
	public int TotalSteps { get; } = totalSteps;

	/// <summary>Human-readable step name (e.g., "Tap HamburgerMenu").</summary>
	public string StepName { get; } = stepName;
}
