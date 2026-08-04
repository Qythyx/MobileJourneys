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
	// public ScreenshotStorage Storage => field ??= storage ?? FilesystemScreenshotStorage.Default();
	public ScreenshotStorage Storage { get; init; } = FilesystemScreenshotStorage.Default();

	/// <summary>
	/// Creates the stand-in backend one fixture's journeys run against, given the fixture and the id
	/// of the device hosting it. <c>null</c> for a suite whose app needs no backend.
	/// </summary>
	public Func<PlatformConfig, string, IJourneyBackend>? CreateBackend { get; init; }

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
