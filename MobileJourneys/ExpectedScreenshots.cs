namespace MobileJourneys;

/// <summary>
/// Index of every screenshot file a set of journeys can produce: the expected baseline
/// locations, and — per container — the steps and journeys whose failure artifacts may
/// legitimately appear there. Shared by the extraneous-file scan and the viewer manifest
/// so both classify stored files identically.
/// </summary>
internal sealed class ExpectedScreenshots
{
	private readonly HashSet<(string Container, string StepName)> _baselines = [];
	private readonly Dictionary<string, List<(string StepName, string JourneyName)>> _stepsByContainer = [];

	/// <summary>Builds the index from the journeys' expected step locations.</summary>
	/// <param name="journeys">The journey definitions to index.</param>
	internal ExpectedScreenshots(IEnumerable<JourneyDefinition> journeys)
	{
		foreach (var journey in journeys)
		{
			foreach (var (container, stepName) in journey.ExpectedStepLocations())
			{
				_ = _baselines.Add((container, stepName));
				if (!_stepsByContainer.TryGetValue(container, out var steps))
				{
					steps = [];
					_stepsByContainer[container] = steps;
				}
				steps.Add((stepName, journey.Name));
			}
		}
	}

	/// <summary>The expected baseline locations, as (container, step-name) pairs.</summary>
	internal IReadOnlyCollection<(string Container, string StepName)> BaselineLocations => _baselines;

	/// <summary>Returns <c>true</c> when the journey runs a step with the given name in the container — i.e., failure artifacts attributed to that triple are legitimate.</summary>
	/// <param name="container">The container path.</param>
	/// <param name="stepName">The step's numbered name.</param>
	/// <param name="journeyName">The journey to check.</param>
	internal bool IsExpectedStep(string container, string stepName, string journeyName) =>
		_stepsByContainer.TryGetValue(container, out var steps) && steps.Contains((stepName, journeyName));

	/// <summary>
	/// Returns <c>true</c> when a stored file is accounted for: a baseline of an expected step,
	/// or a failure artifact attributed to a journey that runs an expected step in the container.
	/// </summary>
	/// <param name="container">The file's container path.</param>
	/// <param name="fileName">The file's name.</param>
	internal bool IsExpected(string container, string fileName) =>
		ArtifactNaming.IsBaseline(fileName)
			? _baselines.Contains((container, ArtifactNaming.BaselineStepName(fileName)))
			: _stepsByContainer.TryGetValue(container, out var steps)
				&& steps.Any(s => ArtifactNaming.IsFailureArtifactForStep(fileName, s.StepName, s.JourneyName));
}
