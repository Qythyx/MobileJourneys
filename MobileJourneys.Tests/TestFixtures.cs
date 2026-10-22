namespace MobileJourneys.Tests;

/// <summary>Platform fixtures for tests whose subject does not depend on the fixture's details.</summary>
internal static class TestFixtures
{
	/// <summary>An Android fixture on one device, light theme.</summary>
	public static readonly AndroidPlatformConfig Android = new(
		"15",
		"Pixel",
		"avd",
		IsLightTheme: true,
		"com.example.app",
		"/path/app.apk",
		null,
		200,
		550,
		3 * 10,
		0.005
	);
}
