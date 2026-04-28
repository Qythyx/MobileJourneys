namespace MobileJourneys;

/// <summary>
/// An action to execute during a journey test step. Actions mutate state — they tap,
/// type, swipe, dismiss, navigate. Pair with <see cref="Expectation"/>s on a
/// <see cref="JourneyStep"/> to verify the resulting state.
/// </summary>
/// <param name="Target">Optional identifier used to build the step label. When the target
/// contains underscores (e.g., "HowItWorks_Carousel"), only the last segment is used
/// (e.g., "Carousel").</param>
public abstract record JourneyAction(string? Target = null) : JourneyOp(Target)
{
	/// <summary>
	/// Executes this action using the provided test driver for simulator/emulator interaction.
	/// </summary>
	/// <param name="driver">The test driver providing access to the app and device.</param>
	public abstract void Execute(TestDriver driver);
}
