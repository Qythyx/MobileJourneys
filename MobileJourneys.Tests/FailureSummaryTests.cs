using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Covers the failure-aggregation half of <see cref="FailureSummary"/>. The Print path
/// runs at <c>AppDomain.ProcessExit</c> and isn't unit-testable in-process — but
/// RecordFailure is the load-bearing entry point that <see cref="Framework.MtpReporter"/>
/// calls, and it must aggregate by config.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class FailureSummaryTests
{
	[SetUp]
	public void Reset() => FailureSummary.ResetForTest();

	[OneTimeTearDown]
	public void Cleanup() =>
		// FailureSummary registers a ProcessExit hook that prints any remaining failures
		// to stderr. Clear the dictionary so the test process doesn't emit a spurious
		// "FAILED JOURNEYS" banner when the runner exits.
		FailureSummary.ResetForTest();

	private static readonly IosPlatformConfig IosFixture = new(
		"26.2",
		"iPhone 16",
		IsLightTheme: true,
		"com.example.app",
		"/path/to/app.app",
		MaxScreenshotHeight: 2000
	);

	private static readonly AndroidPlatformConfig AndroidFixture = new(
		"15",
		"Pixel",
		"Pixel_API35",
		IsLightTheme: false,
		"com.example.app",
		"/path/to/app.apk",
		null,
		MaxScreenshotHeight: 2000
	);

	private sealed record TestEnv : IJourneyEnvironment
	{
		public string Name => "Test";

		public IReadOnlyDictionary<string, string> GetEnvVars() => new Dictionary<string, string>();

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpect() : Expectation
	{
		public override void Verify(TestDriver driver) { }
	}

	private static TestCase MakeCase(PlatformConfig config, string journeyName) =>
		new(config, new JourneyDefinition(new TestEnv(), [new TestExpect()], [], journeyName));

	[Test]
	public void RecordFailureFirstCallAddsConfigEntry()
	{
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Login"));

		var snapshot = FailureSummary.SnapshotForTest();
		_ = snapshot.Should().HaveCount(1);
		_ = snapshot[IosFixture.ToString()].Should().BeEquivalentTo(["Login"]);
	}

	[Test]
	public void RecordFailureMultipleJourneysSameConfigAggregateUnderOneKey()
	{
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Login"));
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Logout"));
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Settings"));

		var snapshot = FailureSummary.SnapshotForTest();
		_ = snapshot.Should().HaveCount(1);
		_ = snapshot[IosFixture.ToString()].Should().BeEquivalentTo(["Login", "Logout", "Settings"]);
	}

	[Test]
	public void RecordFailureDifferentConfigsStayInSeparateEntries()
	{
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Login"));
		FailureSummary.RecordFailure(MakeCase(AndroidFixture, "Login"));

		var snapshot = FailureSummary.SnapshotForTest();
		_ = snapshot.Should().HaveCount(2);
		_ = snapshot[IosFixture.ToString()].Should().BeEquivalentTo(["Login"]);
		_ = snapshot[AndroidFixture.ToString()].Should().BeEquivalentTo(["Login"]);
	}
}
