using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
[NonParallelizable]
public sealed class RunReporterTests
{
	[SetUp]
	public void Reset() => FailureSummary.ResetForTest();

	[Test]
	public void InterruptedRunExitsWithTheInterruptedCode()
	{
		using var log = new TestRunLog();
		var reporter = new ConsoleReporter(log.Log);

		reporter.Interrupted();

		_ = reporter.Summarize().Should().Be(RunReporter.InterruptedExitCode);
	}

	[Test]
	public void RunCutShortByAFaultFailsWithNoJourneyFailed()
	{
		using var log = new TestRunLog();
		var reporter = new ConsoleReporter(log.Log);

		reporter.Fault(new InvalidOperationException("the server went away"));

		_ = reporter.Summarize().Should().Be(1);
	}

	[Test]
	public void RunInterruptedAfterAFaultExitsWithTheInterruptedCode()
	{
		using var log = new TestRunLog();
		var reporter = new ConsoleReporter(log.Log);
		reporter.Fault(new InvalidOperationException("the server went away"));

		reporter.Interrupted();

		_ = reporter.Summarize().Should().Be(RunReporter.InterruptedExitCode);
	}

	[Test]
	public void AnEventIsWrittenToTheRunLog()
	{
		using var log = new TestRunLog();
		var reporter = new ConsoleReporter(log.Log);

		reporter.FixtureRetrying(TestFixtures.Android, 1, "its device stopped responding");

		_ = log.Text.Should().Contain("retrying: its device stopped responding");
	}

	[Test]
	public void DurationUnderAMinuteReadsInTenthsOfASecond() =>
		_ = RunReporter.Duration(TimeSpan.FromMilliseconds(12_340)).Should().Be("12.3s");

	[Test]
	public void DurationFromAMinuteReadsInMinutesAndSeconds() =>
		_ = RunReporter.Duration(TimeSpan.FromSeconds(1023)).Should().Be("17m 3s");
}
