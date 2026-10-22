using Microsoft.Extensions.Time.Testing;
using MobileJourneys.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Wraps a <see cref="RunLog"/> that writes to a writer the test supplies, or to memory, and stamps
/// every entry with <see cref="MondayMorning"/>.
/// </summary>
/// <param name="writer">Where the entries go.</param>
internal sealed class TestRunLog(StringWriter writer) : IDisposable
{
	/// <summary>The time every entry is stamped with.</summary>
	public static readonly DateTimeOffset MondayMorning = new(2026, 1, 5, 9, 30, 0, TimeSpan.Zero);

	/// <summary>Opens a log that writes to memory.</summary>
	public TestRunLog()
		: this(new StringWriter()) { }

	/// <summary>The log under test, or for the code under test to write to.</summary>
	public RunLog Log { get; } = new(writer, "/logs/run.log", Clock());

	/// <summary>Everything written to the log so far.</summary>
	public string Text => writer.ToString();

	/// <inheritdoc/>
	public void Dispose() => Log.Dispose();

	/// <summary>Builds a clock that stands still at <see cref="MondayMorning"/>, in UTC.</summary>
	private static FakeTimeProvider Clock()
	{
		var clock = new FakeTimeProvider(MondayMorning);
		clock.SetLocalTimeZone(TimeZoneInfo.Utc);
		return clock;
	}
}
