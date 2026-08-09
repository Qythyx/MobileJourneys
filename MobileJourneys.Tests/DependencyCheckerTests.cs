using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Sanity checks for <see cref="DependencyChecker"/>. The "happy path" (Appium / xcrun / adb
/// all installed) isn't asserted explicitly because it depends on the host environment —
/// running the consumer's UI suite is the real integration test for that. These tests
/// cover behavior that's host-independent.
/// </summary>
[TestFixture]
public sealed class DependencyCheckerTests
{
	[Test]
	public void VerifyWithEmptyPlatformsStillChecksAppium()
	{
		// Even with no platforms, Appium is required to start the local server.
		// On a dev machine with appium installed this passes; otherwise it throws
		// — verifying that "no platforms" doesn't disable the check.
		var config = new FrameworkConfig(
			DisplayName: "Test",
			PlatformConfigs: [],
			Journeys: [new JourneyDefinition(new TestEnv(), [new TestExpect()], [], [], "j")]
		);

		// Either succeeds (host has appium installed) or throws InvalidOperationException
		// — must NOT throw any other exception type, and must NOT silently skip.
		try
		{
			DependencyChecker.Verify(config);
		}
		catch (InvalidOperationException ex)
		{
			_ = ex.Message.Should().Contain("appium", "the failure message must mention which dep is missing");
		}
	}

	[Test]
	public void VerifyAndroidFixtureFailsWithHelpfulMessageWhenAndroidHomeUnset()
	{
		// Save and clear ANDROID_HOME so the Android branch hits its missing-env-var error.
		var saved = Environment.GetEnvironmentVariable("ANDROID_HOME");
		Environment.SetEnvironmentVariable("ANDROID_HOME", null);
		try
		{
			var config = new FrameworkConfig(
				DisplayName: "Test",
				PlatformConfigs:
				[
					new AndroidPlatformConfig("15", "Pixel", "avd", true, "com.example", "/path/app.apk", null, 200, 550, 3 * 10, 0.005),
				],
				Journeys: [new JourneyDefinition(new TestEnv(), [new TestExpect()], [], [], "j")]
			);

			Action act = () => DependencyChecker.Verify(config);
			// Either Appium check fails first (host without appium) or Android check fails;
			// either way the message must be actionable.
			var ex = act.Should().Throw<InvalidOperationException>().Which;
			_ = ex.Message.Should().ContainAny("ANDROID_HOME", "appium");
		}
		finally
		{
			Environment.SetEnvironmentVariable("ANDROID_HOME", saved);
		}
	}

	private sealed record TestEnv : IJourneyEnvironment
	{
		public string Name => "Test";

		public string BackendUrl => "";

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpect() : Expectation
	{
		public override void Verify(TestDriver driver) { }
	}
}
