namespace MobileJourneys;

/// <summary>
/// An action to execute during a journey test step.
/// </summary>
/// <param name="Target">Optional identifier used to build the step label. When the target contains
/// underscores (e.g., "HowItWorks_Carousel"), only the last segment is used (e.g., "Carousel").</param>
public abstract record JourneyAction(string? Target = null)
{
	private static readonly HashSet<char> InvalidChars = [.. Path.GetInvalidFileNameChars()];

	/// <summary>
	/// Type-prefix used in <see cref="Label"/>. Defaults to the runtime type's simple name;
	/// override to give a wrapper class the same label as the underlying action (e.g., a
	/// localized variant overrides this to return the framework type's name so screenshot
	/// baselines stay stable).
	/// </summary>
	protected virtual string Name => GetType().Name;

	/// <summary>
	/// Step label derived from <see cref="Name"/> and optional <see cref="Target"/>.
	/// Used to build screenshot baseline filenames.
	/// </summary>
	public string Label
	{
		get
		{
			if (Target is null)
			{
				return Name;
			}

			var lastUnderscore = Target.LastIndexOf('_');
			var shortId = lastUnderscore >= 0 ? Target[(lastUnderscore + 1)..] : Target;
			var label = $"{Name} {shortId}";
			return string.Concat(label.Select(c => InvalidChars.Contains(c) ? '_' : c));
		}
	}

	/// <summary>
	/// Executes this action using the provided test driver for simulator/emulator interaction.
	/// </summary>
	/// <param name="driver">The test driver providing access to the app and device.</param>
	public abstract void Execute(TestDriver driver);
}
