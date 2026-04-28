namespace MobileJourneys.Framework;

internal sealed record JourneyResult(
	TestCase TestCase,
	bool Passed,
	TimeSpan Duration,
	string Explanation,
	Exception? Exception
);
