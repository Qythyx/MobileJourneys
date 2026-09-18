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
}
