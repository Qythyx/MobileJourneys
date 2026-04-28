namespace MobileJourneys;

/// <summary>
/// Per-journey app environment passed to the device on launch. Implementations encode
/// app-specific mock state (e.g., logged-in flag, language, theme) and convert it to
/// environment variables read by the app's mock service.
/// </summary>
public interface IJourneyEnvironment
{
	/// <summary>
	/// A short identifier shown in failure messages and step names.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Returns the environment variables to pass to the app on launch. Keys are
	/// already prefixed/cased per the app's mock-env convention.
	/// </summary>
	IReadOnlyDictionary<string, string> GetEnvVars();

	/// <summary>
	/// Returns a copy of this environment specialized for the given fixture (platform +
	/// theme). Implementations typically pin language or other fixture-derived state here.
	/// </summary>
	/// <param name="config">The platform fixture being executed.</param>
	IJourneyEnvironment ForFixture(PlatformConfig config);
}
