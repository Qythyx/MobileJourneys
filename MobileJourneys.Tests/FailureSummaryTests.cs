using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Covers <see cref="FailureSummary"/>. <see cref="FailureSummary.RecordFailure"/> is the
/// load-bearing entry point that <see cref="RunReporter.JourneyCompleted"/> calls;
/// <see cref="FailureSummary.Print"/> emits the end-of-run banner that groups failures by fixture.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class FailureSummaryTests
{
	[SetUp]
	public void Reset() => FailureSummary.ResetForTest();

	private sealed record TestEnv : IJourneyEnvironment
	{
		public string Name => "Test";

		public string BackendUrl => "";
	}

	private sealed record TestExpect() : Expectation
	{
		public override void Verify(TestDriver driver) { }
	}

	private static TestCase MakeCase(PlatformConfig config, string journeyName) =>
		new(config, new JourneyDefinition(new TestEnv(), [new TestExpect()], [], [], journeyName));

	[Test]
	public void RecordFailureFirstCallAddsConfigEntry()
	{
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Login"));

		var snapshot = FailureSummary.SnapshotForTest();
		_ = snapshot.Should().HaveCount(1);
		_ = snapshot[TestFixtures.Ios.ToString()].Should().BeEquivalentTo(["Login"]);
	}

	[Test]
	public void RecordFailureMultipleJourneysSameConfigAggregateUnderOneKey()
	{
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Login"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Logout"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Settings"));

		var snapshot = FailureSummary.SnapshotForTest();
		_ = snapshot.Should().HaveCount(1);
		_ = snapshot[TestFixtures.Ios.ToString()].Should().BeEquivalentTo(["Login", "Logout", "Settings"]);
	}

	[Test]
	public void RecordFailureDifferentConfigsStayInSeparateEntries()
	{
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Login"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Android, "Login"));

		var snapshot = FailureSummary.SnapshotForTest();
		_ = snapshot.Should().HaveCount(2);
		_ = snapshot[TestFixtures.Ios.ToString()].Should().BeEquivalentTo(["Login"]);
		_ = snapshot[TestFixtures.Android.ToString()].Should().BeEquivalentTo(["Login"]);
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
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Login"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Logout"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Android, "Login"));
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		_ = sw.ToString().Should().Contain("FAILED JOURNEYS (3)");
	}

	[Test]
	public void PrintGroupsJourneysUnderTheirConfigName()
	{
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Login"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Logout"));
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		var output = sw.ToString();
		_ = output.Should().Contain(TestFixtures.Ios.ToString());
		_ = output.Should().Contain("    - Login");
		_ = output.Should().Contain("    - Logout");
	}

	[Test]
	public void PrintSortsConfigsAlphabetically()
	{
		// Insertion order is iOS first, but Android sorts before iOS — verify the banner
		// reflects sort order, not insertion order, so reports are stable across runs.
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Login"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Android, "Login"));
		using var sw = new StringWriter();

		FailureSummary.Print(sw);

		var output = sw.ToString();
		var androidIdx = output.IndexOf(TestFixtures.Android.ToString(), StringComparison.Ordinal);
		var iosIdx = output.IndexOf(TestFixtures.Ios.ToString(), StringComparison.Ordinal);
		_ = androidIdx.Should().BeGreaterThanOrEqualTo(0);
		_ = iosIdx.Should().BeGreaterThanOrEqualTo(0);
		_ = androidIdx.Should().BeLessThan(iosIdx);
	}

	[Test]
	public void PrintSortsJourneysAlphabeticallyWithinAConfig()
	{
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Zeta"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Alpha"));
		FailureSummary.RecordFailure(MakeCase(TestFixtures.Ios, "Mu"));
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
