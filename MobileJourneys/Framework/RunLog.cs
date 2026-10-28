using System.Globalization;

namespace MobileJourneys.Framework;

/// <summary>
/// Writes a run's timeline to a file: one entry per event, stamped with the time and with the run,
/// fixture or worker it concerns, its lines after the first indented under it.
/// </summary>
/// <remarks>
/// Entries may be written from several threads at once; each is written whole under one lock, so
/// entries never interleave. A write that fails stops the log rather than the run, and
/// <see cref="Failure"/> says why. A write after <see cref="Dispose"/> is dropped.
/// </remarks>
/// <param name="writer">Where the entries go.</param>
/// <param name="filePath">The file <paramref name="writer"/> writes to, for the reader to be pointed at.</param>
/// <param name="clock">The clock entries are stamped from.</param>
internal sealed class RunLog(TextWriter writer, string filePath, TimeProvider clock) : IDisposable
{
	private readonly Lock gate = new();

	private bool closed;

	/// <summary>The file the log is written to.</summary>
	public string FilePath { get; } = filePath;

	/// <summary>Why the log stopped being written, or <c>null</c> while every write has succeeded.</summary>
	public string? Failure { get; private set; }

	/// <summary>
	/// Opens a new log file, replacing one already at <paramref name="filePath"/>, and flushes it after
	/// every line, so a run killed mid-way keeps every line written before.
	/// </summary>
	/// <param name="filePath">The file to write.</param>
	/// <returns>The log.</returns>
	public static RunLog Open(string filePath) =>
		new(new StreamWriter(filePath, false) { AutoFlush = true }, filePath, TimeProvider.System);

	/// <summary>Writes something that happened to the run as a whole.</summary>
	/// <param name="text">What happened; lines after the first are indented under it.</param>
	public void Write(string text) => WriteEntry("run", text);

	/// <summary>Writes something that happened to a fixture, or to one of its devices when no worker is known.</summary>
	/// <param name="fixture">The fixture.</param>
	/// <param name="text">What happened; lines after the first are indented under it.</param>
	public void Write(PlatformConfig fixture, string text) => WriteEntry(fixture.DeviceName, text);

	/// <summary>Writes something that happened to one of a fixture's workers.</summary>
	/// <param name="fixture">The fixture the worker belongs to.</param>
	/// <param name="worker">The worker's 1-based index.</param>
	/// <param name="text">What happened; lines after the first are indented under it.</param>
	public void Write(PlatformConfig fixture, int worker, string text) =>
		WriteEntry($"{fixture.DeviceName} #{worker}", text);

	/// <inheritdoc/>
	public void Dispose()
	{
		lock (gate)
		{
			closed = true;
			writer.Dispose();
		}
	}

	private void WriteEntry(string source, string text)
	{
		var time = clock.GetLocalNow().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
		var lines = text.TrimEnd().Split('\n');
		lock (gate)
		{
			if (closed || Failure is not null)
			{
				return;
			}

			try
			{
				writer.WriteLine($"{time}  {source}  {lines[0]}");
				foreach (var line in lines.Skip(1))
				{
					writer.WriteLine($"    {line.TrimEnd()}");
				}
			}
			catch (IOException ex)
			{
				Failure = $"the run log stopped being written at {time}: {ex.Message}";
			}
		}
	}
}
