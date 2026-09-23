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
		var reporter = new ConsoleReporter();

		reporter.Interrupted();

		_ = reporter.Summarize().Should().Be(RunReporter.InterruptedExitCode);
	}

	[Test]
	public void DurationUnderAMinuteReadsInTenthsOfASecond() =>
		_ = RunReporter.Duration(TimeSpan.FromMilliseconds(12_340)).Should().Be("12.3s");

	[Test]
	public void DurationFromAMinuteReadsInMinutesAndSeconds() =>
		_ = RunReporter.Duration(TimeSpan.FromSeconds(1023)).Should().Be("17m 3s");
}
