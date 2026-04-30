using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Covers <see cref="FailureSummary"/>. RecordFailure is the load-bearing entry point that
/// <see cref="Framework.MtpReporter"/> calls; Print emits the end-of-run banner that
/// summarizes failures grouped by fixture.
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

	[Test]
	public void PrintWritesNothingWhenNoFailuresRecorded()
	{
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		_ = sw.ToString().Should().BeEmpty();
	}

	[Test]
	public void PrintBannerIncludesTotalFailureCount()
	{
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Login"));
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Logout"));
		FailureSummary.RecordFailure(MakeCase(AndroidFixture, "Login"));
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		_ = sw.ToString().Should().Contain("FAILED JOURNEYS (3)");
	}

	[Test]
	public void PrintGroupsJourneysUnderTheirConfigName()
	{
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Login"));
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Logout"));
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		var output = sw.ToString();
		_ = output.Should().Contain(IosFixture.ToString());
		_ = output.Should().Contain("    - Login");
		_ = output.Should().Contain("    - Logout");
	}

	[Test]
	public void PrintSortsConfigsAlphabetically()
	{
		// Insertion order is iOS first, but Android sorts before iOS — verify the banner
		// reflects sort order, not insertion order, so reports are stable across runs.
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Login"));
		FailureSummary.RecordFailure(MakeCase(AndroidFixture, "Login"));
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		var output = sw.ToString();
		var androidIdx = output.IndexOf(AndroidFixture.ToString(), StringComparison.Ordinal);
		var iosIdx = output.IndexOf(IosFixture.ToString(), StringComparison.Ordinal);
		_ = androidIdx.Should().BeGreaterThanOrEqualTo(0);
		_ = iosIdx.Should().BeGreaterThanOrEqualTo(0);
		_ = androidIdx.Should().BeLessThan(iosIdx);
	}

	[Test]
	public void PrintSortsJourneysAlphabeticallyWithinAConfig()
	{
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Zeta"));
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Alpha"));
		FailureSummary.RecordFailure(MakeCase(IosFixture, "Mu"));
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		var output = sw.ToString();
		var alphaIdx = output.IndexOf("- Alpha", StringComparison.Ordinal);
		var muIdx = output.IndexOf("- Mu", StringComparison.Ordinal);
		var zetaIdx = output.IndexOf("- Zeta", StringComparison.Ordinal);
		_ = alphaIdx.Should().BeLessThan(muIdx);
		_ = muIdx.Should().BeLessThan(zetaIdx);
	}
}
