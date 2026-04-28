using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace MobileJourneys;

internal static class FailureSummary
{
	private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> Failures = new(StringComparer.Ordinal);

	static FailureSummary() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Print();

	internal static void RecordFailure(string config, string journeyName) =>
		Failures.GetOrAdd(config, _ => []).Add(journeyName);

	private static void Print()
	{
		if (Failures.IsEmpty)
		{
			return;
		}

		var totalFailures = 0;
		var sorted = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
		foreach (var (config, journeys) in Failures)
		{
			var journeySet = new SortedSet<string>(journeys, StringComparer.Ordinal);
			sorted[config] = journeySet;
			totalFailures += journeySet.Count;
		}

		var builder = new StringBuilder();
		_ = builder
			.AppendLine(" ")
			.AppendLine("======================================")
			.AppendLine(CultureInfo.InvariantCulture, $"  FAILED JOURNEYS ({totalFailures})")
			.AppendLine("======================================");
		foreach (var (config, journeys) in sorted)
		{
			_ = builder.AppendLine(config);
			foreach (var journey in journeys)
			{
				_ = builder.AppendLine(CultureInfo.InvariantCulture, $"    - {journey}");
			}
		}
		_ = builder.AppendLine("======================================").AppendLine(" ");

		Console.Error.Write(builder);
	}
}
