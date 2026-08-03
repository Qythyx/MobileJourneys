namespace MobileJourneys;

/// <summary>
/// Shared base for <see cref="JourneyAction"/> and <see cref="Expectation"/>. Provides
/// the <see cref="Label"/> formatting used in step names and screenshot baseline
/// filenames, and the <see cref="Name"/> hook subclasses can override to share a label
/// prefix (e.g., a wrapper class can override <see cref="Name"/> so its baseline files
/// match its parent's).
/// </summary>
/// <param name="Target">Optional identifier used to build the step label. When the target
/// contains underscores (e.g., "ActivityOverlay_Overlay"), only the last segment is used
/// (e.g., "Overlay").</param>
public abstract record JourneyOp(string? Target = null)
{
	private static readonly HashSet<char> InvalidChars = [.. Path.GetInvalidFileNameChars()];

	/// <summary>
	/// Type-prefix used in <see cref="Label"/>. Defaults to the runtime type's simple name;
	/// override to give a wrapper class the same label as the underlying op (e.g., a
	/// localized variant overrides this so screenshot baselines stay stable). Public so a wrapper
	/// can forward the op it wraps rather than guess at its name.
	/// </summary>
	public virtual string Name => GetType().Name;

	/// <summary>
	/// Step label derived from <see cref="Name"/> and optional <see cref="Target"/>.
	/// Used to build screenshot baseline filenames. Filename-invalid characters in the
	/// target are replaced with underscores.
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
}
