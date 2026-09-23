using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class FixtureTimingsTests
{
	private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

	[Test]
	public void LookupMedianIsTheMiddleRoundTrip()
	{
		var timings = new FixtureTimings(Budget);
		foreach (var ms in new[] { 900, 100, 300 })
		{
			timings.RecordLookup(TimeSpan.FromMilliseconds(ms));
		}

		_ = timings.Summarize().LookupMedian.Should().Be(TimeSpan.FromMilliseconds(300));
	}

	[Test]
	public void LongestWaitCarriesWhatItWasFor()
	{
		var timings = new FixtureTimings(Budget);
		timings.RecordWait(WaitKind.Element, TimeSpan.FromSeconds(2), true);
		timings.RecordWait(WaitKind.Screen, TimeSpan.FromSeconds(9), true);
		timings.RecordWait(WaitKind.Alert, TimeSpan.FromSeconds(4), true);

		var summary = timings.Summarize();

		_ = summary.WaitMax.Should().Be(TimeSpan.FromSeconds(9));
		_ = summary.LongestWaitKind.Should().Be(WaitKind.Screen);
	}

	[Test]
	public void WaitsThatRanOutAreCountedApartFromTheLongest()
	{
		var timings = new FixtureTimings(Budget);
		timings.RecordWait(WaitKind.Element, TimeSpan.FromSeconds(3), true);
		timings.RecordWait(WaitKind.Element, Budget, false);

		var summary = timings.Summarize();

		_ = summary.WaitsTimedOut.Should().Be(1);
		_ = summary.WaitMax.Should().Be(TimeSpan.FromSeconds(3));
	}

	[Test]
	public void WaitAverageCoversOnlyTheWaitsThatSucceeded()
	{
		var timings = new FixtureTimings(Budget);
		timings.RecordWait(WaitKind.Element, TimeSpan.FromSeconds(2), true);
		timings.RecordWait(WaitKind.Element, TimeSpan.FromSeconds(4), true);
		timings.RecordWait(WaitKind.Element, Budget, false);

		_ = timings.Summarize().WaitAverage.Should().Be(TimeSpan.FromSeconds(3));
	}
}
