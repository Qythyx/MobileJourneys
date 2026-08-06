namespace MobileJourneys;

/// <summary>
/// Per-journey app state. Implementations hold whatever one journey needs arranged.
/// </summary>
public interface IJourneyEnvironment
{
	/// <summary>
	/// A short identifier shown in failure messages and step names.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// The address of the backend this journey's app should talk to, handed over at launch as a
	/// process environment variable on iOS and as an intent string extra on Android, under the name
	/// <see cref="FrameworkConfig.BackendSetup.UrlVariable"/> gives.
	/// </summary>
	string BackendUrl { get; }

	/// <summary>
	/// Returns a copy of this environment specialized for the given fixture (platform +
	/// theme). Implementations typically pin language or other fixture-derived state here.
	/// </summary>
	/// <param name="config">The platform fixture being executed.</param>
	IJourneyEnvironment ForFixture(PlatformConfig config);
}
