using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using MobileJourneys.Framework;

namespace MobileJourneys;

internal static class FailureSummary
{
	private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> Failures = new(StringComparer.Ordinal);

	static FailureSummary() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Print(Console.Error);

	internal static void RecordFailure(TestCase testCase) =>
		Failures.GetOrAdd(testCase.Config.ToString(), _ => []).Add(testCase.Journey.Name);

	internal static IReadOnlyDictionary<string, IReadOnlyCollection<string>> SnapshotForTest() =>
		Failures.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<string>)[.. kv.Value]);

	internal static void ResetForTest() => Failures.Clear();

	internal static void Print(TextWriter writer)
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

		writer.Write(builder);
	}
}
