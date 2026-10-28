using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class RunLogTests
{
	[Test]
	public void WriteStampsTheLineWithTheTimeAndTheRun()
	{
		using var log = new TestRunLog();

		log.Log.Write("started");

		_ = log.Text.Should().Be("09:30:00.000  run  started\n");
	}

	[Test]
	public void WriteIndentsTheLinesAfterTheFirst()
	{
		using var log = new TestRunLog();

		log.Log.Write("failed:\nat Somewhere()");

		_ = log.Text.Should().EndWith("failed:\n    at Somewhere()\n");
	}

	[Test]
	public void WriteForAWorkerNamesItsFixtureAndItsNumber()
	{
		using var log = new TestRunLog();

		log.Log.Write(TestFixtures.Android, 2, "device up");

		_ = log.Text.Should().Contain($"  {TestFixtures.Android.DeviceName} #2  device up");
	}

	[Test]
	public void WriteThatFailsSaysWhyTheLogStopped()
	{
		using var log = new TestRunLog(new FailingWriter());

		log.Log.Write("started");

		_ = log.Log.Failure.Should().Contain("disk full");
	}

	[Test]
	public void WriteThatFailsDoesNotThrow()
	{
		using var log = new TestRunLog(new FailingWriter());

		_ = FluentActions.Invoking(() => log.Log.Write("started")).Should().NotThrow();
	}

	[Test]
	public void WriteAfterAFailureIsNotAttempted()
	{
		var writer = new FailingWriter();
		using var log = new TestRunLog(writer);
		log.Log.Write("started");

		log.Log.Write("still going");

		_ = writer.Attempts.Should().Be(1);
	}

	[Test]
	public void WriteAfterDisposeIsDropped()
	{
		var log = new TestRunLog();
		log.Dispose();

		_ = FluentActions.Invoking(() => log.Log.Write("late")).Should().NotThrow();
	}

	/// <summary>A writer that fails every line it is given, as one writing to a full disk does.</summary>
	private sealed class FailingWriter : StringWriter
	{
		/// <summary>How many lines it has been given.</summary>
		public int Attempts { get; private set; }

		public override void WriteLine(string? value)
		{
			Attempts++;
			throw new IOException("disk full");
		}
	}
}
