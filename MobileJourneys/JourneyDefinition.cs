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
public sealed record JourneyDefinition(
	IJourneyEnvironment Scenario,
	Expectation[] InitialExpect,
	JourneyStep[] Steps,
	[CallerMemberName] string Name = ""
)
{
	public Expectation[] InitialExpect { get; } =
		InitialExpect.Length > 0
			? InitialExpect
			: throw new ArgumentException("Must contain at least one element.", nameof(InitialExpect));

	/// <summary>
	/// Step name for the initial screenshot, derived from the first expectation.
	/// </summary>
	public string InitialName => InitialExpect[0].Label;

	public override string ToString() => Name;
}
