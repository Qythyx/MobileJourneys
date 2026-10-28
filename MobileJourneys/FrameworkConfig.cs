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
	/// The journey definitions to discover and execute, in the order they run: longest first, so
	/// that a long one is never picked up last and left running alone while the fixture's other
	/// devices sit idle. Journeys with the same number of steps keep the order they were given in.
	/// Validated at construction to have distinct names — journey names key test identity,
	/// artifact attribution, and (for tree-defined journeys) the screenshot folder layout.
	/// </summary>
	public IReadOnlyList<JourneyDefinition> Journeys { get; } =
	[
		.. EnsureDistinct(Journeys, journey => journey.Name, "Journey names must be unique", nameof(Journeys))
			.OrderByDescending(journey => journey.ExpectedStepLocations().Count()),
	];

	/// <summary>
	/// The fixture matrix the suite runs against. Validated at construction so that no two fixtures share
	/// a <see cref="PlatformConfig.DeviceName"/> or a device.
	/// </summary>
	public IReadOnlyList<PlatformConfig> PlatformConfigs { get; } =
		EnsureDistinct(
			EnsureDistinct(
				PlatformConfigs,
				platform => platform.DeviceName,
				"Fixture device names must be unique",
				nameof(PlatformConfigs)
			),
			platform => platform.DeviceKey,
			"Fixtures run at the same time, so each needs devices of its own",
			nameof(PlatformConfigs)
		);

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
	/// Finds the fixture named by its <see cref="PlatformConfig.DeviceName"/>, which is how a
	/// fixture is named by anything outside this process — a page's request, a runner's event.
	/// </summary>
	/// <param name="deviceName">The device name to look for.</param>
	/// <returns>The fixture, or <c>null</c> when the matrix holds none of that name.</returns>
	public PlatformConfig? FindPlatform(string? deviceName) =>
		PlatformConfigs.FirstOrDefault(platform => platform.DeviceName == deviceName);

	/// <summary>
	/// Finds screenshot files no journey references — see <see cref="ScreenshotStorage.FindExtraneous"/>.
	/// </summary>
	/// <param name="deleteExtraneous">When <c>true</c>, deletes the extraneous files after collecting them.</param>
	public List<string> FindExtraneous(bool deleteExtraneous) => Storage.FindExtraneous(this, deleteExtraneous);

	private static IReadOnlyList<T> EnsureDistinct<T>(
		IReadOnlyList<T> items,
		Func<T, string> keyOf,
		string rule,
		string paramName
	)
	{
		List<string> duplicates =
		[
			.. items.GroupBy(keyOf).Where(group => group.Count() > 1).Select(group => group.Key),
		];
		return duplicates.Count == 0
			? items
			: throw new ArgumentException($"{rule}; duplicates: {string.Join(", ", duplicates)}.", paramName);
	}
}
