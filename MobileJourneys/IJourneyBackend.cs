namespace MobileJourneys;

/// <summary>
/// A stand-in backend the app under test talks to. One instance serves one fixture, so fixtures the
/// suite runs concurrently never share data, and it is created only once that fixture's device is up
/// — a backend reachable from a device usually has to bind itself to it.
/// </summary>
public interface IJourneyBackend : IDisposable
{
	/// <summary>
	/// Puts the backend into the state the next journey needs, and returns the environment to launch
	/// the app with: the one passed in, plus whatever only the backend can supply, such as the address
	/// it is answering on.
	/// </summary>
	/// <param name="environment">The journey's environment, already specialized for the fixture.</param>
	/// <returns>The environment the app should launch with.</returns>
	IJourneyEnvironment PrepareFor(IJourneyEnvironment environment);
}
