namespace MobileJourneys;

/// <summary>
/// Configuration the consumer test executable passes into the framework. The framework
/// reads journeys, platform fixtures, and IDE-display strings from this record — no globals
/// or static lookups.
/// </summary>
/// <param name="DisplayName">Shown in the runner header (e.g., "Beerbox UI Tests").</param>
/// <param name="PlatformConfigs">The fixture matrix the suite runs against.</param>
/// <param name="Journeys">The journey definitions to discover and execute.</param>
public sealed record FrameworkConfig(
	string DisplayName,
	IReadOnlyList<PlatformConfig> PlatformConfigs,
	IReadOnlyList<JourneyDefinition> Journeys
)
{
	/// <summary>
	/// The journey definitions to discover and execute. Validated at construction to have
	/// distinct names — journey names key test identity, artifact attribution, and (for
	/// tree-defined journeys) the screenshot folder layout.
	/// </summary>
	public IReadOnlyList<JourneyDefinition> Journeys { get; } = EnsureDistinctNames(Journeys);

	/// <summary>
	/// Optional storage backend for screenshots and journey artifacts. Defaults to a
	/// <see cref="FilesystemScreenshotStorage"/> rooted at the consumer test project's
	/// <c>Screenshots/</c> directory when <c>null</c>.
	/// </summary>
	public ScreenshotStorage Storage { get; init; } = FilesystemScreenshotStorage.Default();

	/// <summary>
	/// The stand-in backend this suite's app talks to, or <c>null</c> for an app that needs none.
	/// </summary>
	public BackendSetup? Backend { get; init; }

	/// <summary>
	/// How to create the backend, and the name the app reads its address under. One value rather than
	/// two, so a suite cannot declare a backend and then launch the app under a name it does not read
	/// — which leaves the app waiting on a backend it never finds, with nothing to say so.
	/// </summary>
	/// <param name="Create">
	/// Builds the backend for one fixture, given that fixture and the id of the device hosting it.
	/// </param>
	/// <param name="UrlVariable">
	/// The name <see cref="IJourneyEnvironment.BackendUrl"/> arrives under. Named by the consumer,
	/// because the app reading it cannot reference this assembly.
	/// </param>
	public sealed record BackendSetup(Func<PlatformConfig, string, IJourneyBackend> Create, string UrlVariable);

	/// <summary>
	/// Finds screenshot files no journey references — see <see cref="ScreenshotStorage.FindExtraneous"/>.
	/// </summary>
	/// <param name="deleteExtraneous">When <c>true</c>, deletes the extraneous files after collecting them.</param>
	public List<string> FindExtraneous(bool deleteExtraneous) => Storage.FindExtraneous(this, deleteExtraneous);

	private static IReadOnlyList<JourneyDefinition> EnsureDistinctNames(IReadOnlyList<JourneyDefinition> journeys)
	{
		var duplicates = journeys.GroupBy(j => j.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
		return duplicates.Count == 0
			? journeys
			: throw new ArgumentException(
				$"Journey names must be unique; duplicates: {string.Join(", ", duplicates)}.",
				nameof(journeys)
			);
	}
}
