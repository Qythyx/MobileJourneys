using System.Runtime.CompilerServices;

namespace MobileJourneys;

/// <summary>
/// A tree of journeys sharing one mock scenario and start state: the root holds the scenario,
/// initial-screen expectations, and any steps common to every journey in the tree; interior
/// <see cref="Branch"/> nodes hold steps shared by several journeys; childless nodes hold the steps
/// unique to one journey. Flattening yields one <see cref="JourneyDefinition"/> per childless node,
/// so the runner and selection machinery see ordinary journeys. Screenshots of shared steps are
/// stored once, in a folder hierarchy that mirrors the tree.
/// </summary>
/// <param name="Scenario">The mock backend configuration for every journey in the tree.</param>
/// <param name="InitialExpect">Expectations for the initial screen before any steps execute.</param>
/// <param name="Steps">Steps common to every journey in the tree, executed after the initial screen.</param>
/// <param name="Children">The nodes that diverge after the root's steps. Empty for a tree that is
/// itself a single journey named after the root.</param>
/// <param name="InitialMaskElements">AutomationIds of elements whose bounds are excluded from the
/// initial screenshot's stabilization check and diff. Equivalent to
/// <see cref="JourneyDefinition"/>'s parameter of the same name.</param>
/// <param name="Name">Auto-populated from the field name via CallerMemberName. Names the root
/// screenshot folder, and — for a childless tree — the journey itself.</param>
public sealed record JourneyTree(
	IJourneyEnvironment Scenario,
	Expectation[] InitialExpect,
	JourneyStep[] Steps,
	Branch[] Children,
	string[]? InitialMaskElements = null,
	[CallerMemberName] string Name = ""
)
{
	/// <summary>The nodes that diverge after the root's steps. Validated for distinct names at construction.</summary>
	public Branch[] Children { get; } = Branch.EnsureDistinctNames(Children, Name);

	/// <summary>
	/// Yields one <see cref="JourneyDefinition"/> per childless node (or a single journey named
	/// after the root when the tree has no children). Each journey's steps are the concatenation of
	/// every node's steps along the root→leaf path, and each step's screenshot container is the
	/// folder path of the node that owns it.
	/// </summary>
	public IEnumerable<JourneyDefinition> Flatten()
	{
		if (Children.Length == 0)
		{
			yield return new(Scenario, InitialExpect, Steps, InitialMaskElements, Name);
			yield break;
		}

		// The prefix containers cover the initial screenshot plus the root's own steps.
		var rootContainers = new List<string>(Enumerable.Repeat(Name, Steps.Length + 1));
		foreach (var journey in FlattenChildren(Children, Name, [.. Steps], rootContainers))
		{
			yield return journey;
		}
	}

	private IEnumerable<JourneyDefinition> FlattenChildren(
		Branch[] children,
		string parentPath,
		List<JourneyStep> prefixSteps,
		List<string> prefixContainers
	)
	{
		foreach (var child in children)
		{
			var path = $"{parentPath}/{child.Name}";
			var steps = new List<JourneyStep>(prefixSteps);
			steps.AddRange(child.Steps);
			var containers = new List<string>(prefixContainers);
			containers.AddRange(Enumerable.Repeat(path, child.Steps.Length));

			// A childless node is terminal — emit its journey; otherwise recurse into its children.
			if (child.Children.Length > 0)
			{
				foreach (var journey in FlattenChildren(child.Children, path, steps, containers))
				{
					yield return journey;
				}
			}
			else
			{
				yield return new(Scenario, InitialExpect, [.. steps], InitialMaskElements, child.Name)
				{
					StepContainers = [.. containers],
				};
			}
		}
	}
}
