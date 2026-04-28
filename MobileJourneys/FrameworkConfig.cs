namespace MobileJourneys;

/// <summary>
/// Configuration the consumer test executable passes into the framework. The framework
/// reads journeys, platform fixtures, deep-link scheme, and IDE-display strings from this
/// record — no globals or static lookups.
/// </summary>
/// <param name="DisplayName">Shown in the test runner header (e.g., "Beerbox UI Tests").</param>
/// <param name="Description">Shown in the test runner help text.</param>
/// <param name="TestNodeNamespace">Namespace used in MTP test-method identity. IDE test
/// explorers (e.g., C# Dev Kit) group tests under this namespace.</param>
/// <param name="DeepLinkScheme">URL scheme without "://" (e.g., "beerbox") used by
/// driver helpers that open in-app deep links (e.g., scroll-to-element).</param>
/// <param name="PlatformConfigs">The fixture matrix the suite runs against.</param>
/// <param name="Journeys">The journey definitions to discover and execute.</param>
public sealed record FrameworkConfig(
	string DisplayName,
	string Description,
	string TestNodeNamespace,
	string DeepLinkScheme,
	IReadOnlyList<PlatformConfig> PlatformConfigs,
	IReadOnlyList<JourneyDefinition> Journeys
);
