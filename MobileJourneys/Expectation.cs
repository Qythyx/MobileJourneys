namespace MobileJourneys;

/// <summary>
/// A typed expectation to verify after executing a journey action.
/// Expectations are processed in array order; the sequence matters for readiness gates.
/// </summary>
/// <param name="Target">Optional identifier used to build the step label. When the target contains
/// underscores (e.g., "ActivityOverlay_Overlay"), only the last segment is used (e.g., "Overlay").</param>
public abstract record Expectation(string? Target = null)
{
	/// <summary>
	/// Type-prefix used in <see cref="Label"/>. Defaults to the runtime type's simple name.
	/// </summary>
	protected virtual string Name => GetType().Name;

	/// <summary>
	/// Step label derived from <see cref="Name"/> and optional <see cref="Target"/>.
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
			return $"{Name} {shortId}";
		}
	}

	/// <summary>
	/// Verifies this expectation using the provided test driver.
	/// </summary>
	/// <param name="driver">The test driver providing access to the app and device.</param>
	public abstract void Verify(TestDriver driver);
}
