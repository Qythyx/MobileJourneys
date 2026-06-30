using System.Runtime.CompilerServices;

namespace MobileJourneys;

/// <summary>
/// Defines a complete journey test: a mock scenario, initial screen expectations,
/// and a sequence of action+expectation steps that each produce a screenshot baseline.
/// </summary>
/// <param name="Scenario">The mock backend configuration for this journey.</param>
/// <param name="InitialExpect">Expectations for the initial screen before any steps execute.</param>
/// <param name="Steps">The sequence of action+expectation steps.</param>
/// <param name="Name">Auto-populated from the property name via CallerMemberName.</param>
/// <param name="InitialMaskElements">AutomationIds of elements whose bounds are excluded from the initial
/// screenshot's stabilization check and diff (e.g., an animated spinner on a scenario that opens directly
/// onto a loading screen). Equivalent to <see cref="JourneyStep.MaskElements"/> but for the initial screen.
/// Specify by name, since it follows the CallerMemberName-populated <paramref name="Name"/>.</param>
public sealed record JourneyDefinition(
	IJourneyEnvironment Scenario,
	Expectation[] InitialExpect,
	JourneyStep[] Steps,
	string[]? InitialMaskElements = null,
	[CallerMemberName] string Name = ""
)
{
	/// <summary>The initial-screen expectations. Validated to be non-empty at construction.</summary>
	public Expectation[] InitialExpect { get; } =
		InitialExpect.Length > 0
			? InitialExpect
			: throw new ArgumentException("Must contain at least one element.", nameof(InitialExpect));

	/// <summary>
	/// Step name for the initial screenshot, derived from the first expectation.
	/// </summary>
	public string InitialName => InitialExpect[0].Label;

	/// <summary>Formats a baseline filename stem from a 1-based step number and a name.</summary>
	public static string FormatStepName(int number, string name) => $"{number:D2} {name}";

	/// <summary>Yields the baseline filename stems (without extension) for every step in this journey, in order.</summary>
	public IEnumerable<string> ExpectedStepNames()
	{
		yield return FormatStepName(1, InitialName);
		for (var i = 0; i < Steps.Length; i++)
		{
			yield return FormatStepName(i + 2, Steps[i].Name);
		}
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
