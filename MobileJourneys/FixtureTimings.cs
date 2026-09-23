using System.Diagnostics;

namespace MobileJourneys;

/// <summary>What a wait is for, as the end-of-run timing report names it.</summary>
public enum WaitKind
{
	/// <summary>An element to appear, or to go.</summary>
	Element,

	/// <summary>An alert to appear.</summary>
	Alert,

	/// <summary>A freshly launched app to reach the foreground and hold still.</summary>
	Launch,

	/// <summary>The screen to match its baseline, or to hold still.</summary>
	Screen,

	/// <summary>A notification banner to appear.</summary>
	Notification,
}

/// <summary>
/// Measures how long one fixture spends waiting on its devices: every lookup's round trip, and
/// every wait for the app to reach a state, against the budget each wait had. A wait that runs out
/// its budget is counted apart from the rest, so the longest wait that succeeded shows how close
/// the fixture came to the budget rather than the budget itself.
/// </summary>
/// <remarks>Shared by the fixture's workers, so every member may be called from several threads at once.</remarks>
/// <param name="waitBudget">How long any one wait may take.</param>
public sealed class FixtureTimings(TimeSpan waitBudget)
{
	private readonly Lock gate = new();

	private readonly List<TimeSpan> lookups = [];

	private readonly List<TimeSpan> waits = [];

	private readonly Stopwatch stopwatch = Stopwatch.StartNew();

	private TimeSpan longestWait;

	private WaitKind longestWaitKind;

	private int waitsTimedOut;

	/// <summary>How long any one wait may take.</summary>
	public TimeSpan WaitBudget { get; } = waitBudget;

	/// <summary>Records one round trip to the device asking for an element, whether or not it was there.</summary>
	/// <param name="elapsed">How long the device took to answer.</param>
	public void RecordLookup(TimeSpan elapsed)
	{
		lock (gate)
		{
			lookups.Add(elapsed);
		}
	}

	/// <summary>Records one wait for the app to reach a state.</summary>
	/// <param name="kind">What the wait was for.</param>
	/// <param name="elapsed">How long it waited.</param>
	/// <param name="succeeded"><c>false</c> when the wait ran out its budget instead.</param>
	public void RecordWait(WaitKind kind, TimeSpan elapsed, bool succeeded)
	{
		lock (gate)
		{
			if (!succeeded)
			{
				waitsTimedOut++;
			}
			else
			{
				waits.Add(elapsed);
				if (elapsed > longestWait)
				{
					longestWait = elapsed;
					longestWaitKind = kind;
				}
			}
		}
	}

	/// <summary>Stops the clock on the fixture's wall time, once its last worker has finished.</summary>
	public void Finish() => stopwatch.Stop();

	/// <summary>Takes stock of what has been recorded so far.</summary>
	/// <returns>The fixture's figures.</returns>
	public Summary Summarize()
	{
		lock (gate)
		{
			return new Summary(
				lookups.Count,
				Median(lookups),
				lookups.Count == 0 ? TimeSpan.Zero : lookups.Max(),
				waits.Count,
				waits.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)waits.Average(wait => wait.Ticks)),
				longestWait,
				longestWaitKind,
				waitsTimedOut,
				stopwatch.Elapsed
			);
		}
	}

	private static TimeSpan Median(List<TimeSpan> values)
	{
		if (values.Count == 0)
		{
			return TimeSpan.Zero;
		}

		var sorted = values.Order().ToList();
		return sorted[(sorted.Count - 1) / 2];
	}

	/// <summary>A fixture's figures at one moment.</summary>
	/// <param name="Lookups">How many round trips were made.</param>
	/// <param name="LookupMedian">The typical round trip.</param>
	/// <param name="LookupMax">The slowest round trip.</param>
	/// <param name="Waits">How many waits succeeded.</param>
	/// <param name="WaitAverage">The average wait that succeeded.</param>
	/// <param name="WaitMax">The longest wait that succeeded.</param>
	/// <param name="LongestWaitKind">What the longest wait was for.</param>
	/// <param name="WaitsTimedOut">How many waits ran out their budget.</param>
	/// <param name="Elapsed">The fixture's wall time, from its devices starting to its last worker finishing.</param>
	public sealed record Summary(
		int Lookups,
		TimeSpan LookupMedian,
		TimeSpan LookupMax,
		int Waits,
		TimeSpan WaitAverage,
		TimeSpan WaitMax,
		WaitKind LongestWaitKind,
		int WaitsTimedOut,
		TimeSpan Elapsed
	);
}
