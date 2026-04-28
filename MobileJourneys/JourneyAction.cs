namespace MobileJourneys;

/// <summary>
/// An action to execute during a journey test step.
/// </summary>
/// <param name="Target">Optional identifier used to build the step name. When the target contains
/// underscores (e.g., "HowItWorks_Carousel"), only the last segment is used (e.g., "Carousel").</param>
public abstract record JourneyAction(string? Target = null)
{
	private static readonly HashSet<char> InvalidChars = [.. Path.GetInvalidFileNameChars()];

	/// <summary>
	/// Step name derived from the concrete type and optional target.
	/// </summary>
	public string Name
	{
		get
		{
			if (Target is null)
			{
				return GetType().Name;
			}

			var lastUnderscore = Target.LastIndexOf('_');
			var shortId = lastUnderscore >= 0 ? Target[(lastUnderscore + 1)..] : Target;
			var name = $"{GetType().Name} {shortId}";
			return string.Concat(name.Select(c => InvalidChars.Contains(c) ? '_' : c));
		}
	}

	/// <summary>
	/// Executes this action using the provided test driver for simulator/emulator interaction.
	/// </summary>
	/// <param name="driver">The test driver providing access to the app and device.</param>
	public abstract void Execute(TestDriver driver);
}
