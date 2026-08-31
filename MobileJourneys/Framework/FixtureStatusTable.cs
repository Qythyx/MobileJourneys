using System.Globalization;
using Spectre.Console;

namespace MobileJourneys.Framework;

/// <summary>
/// A table that redraws in place: one row per platform fixture, showing how many of its journeys are
/// done, still to come, and failed, plus the step it is on right now. Owns the wording of a row's
/// current-activity cell as well as the layout, so both of its drivers — <see cref="LiveStatusReporter"/>
/// for a run in this process, and the review server for the events a rerun posts back — render the
/// same table.
/// </summary>
/// <param name="fixtures">Each fixture the run will use, and how many journeys it will run.</param>
internal sealed class FixtureStatusTable(IEnumerable<(PlatformConfig Config, int Total)> fixtures)
{
	private const int RefreshIntervalMs = 150;

	/// <summary>Narrowest the current-step column is allowed to get on a cramped terminal.</summary>
	private const int MinCurrentWidth = 20;

	/// <summary>What a fixture's row reads once it has no journeys left to run.</summary>
	private const string Finished = "all journeys completed";

	/// <summary>The count columns, which are never wider than their headers.</summary>
	private static readonly string[] CountHeaders = ["Total", "Done", "Left", "Fail"];

	/// <summary>Guards the rows against the fixture threads, and the server's request threads.</summary>
	private readonly Lock gate = new();

	private readonly Dictionary<PlatformConfig, FixtureProgress> rows = fixtures.ToDictionary(
		fixture => fixture.Config,
		fixture => new FixtureProgress(RunReporter.FixtureLabel(fixture.Config), fixture.Total)
	);

	/// <summary>One row of the table: a fixture's totals and what it is doing.</summary>
	/// <param name="name">The fixture's label.</param>
	/// <param name="total">How many journeys it will run.</param>
	private sealed class FixtureProgress(string name, int total)
	{
		public string Name { get; } = name;

		public int Total { get; } = total;

		public int Done { get; set; }

		public int Failed { get; set; }

		/// <summary>
		/// What the fixture is doing. Starts as the device coming up, since that is now the first
		/// thing the table shows rather than something that finished before it appeared.
		/// </summary>
		public string Current { get; set; } = "starting the device…";

		public bool Abandoned { get; set; }
	}

	/// <summary>Keeps the table on screen, redrawing it, for as long as <paramref name="body"/> runs.</summary>
	/// <param name="body">The work whose progress the table is showing.</param>
	/// <returns>A task completing when the body has finished and the display has been torn down.</returns>
	public async Task RunAsync(Func<Task> body) =>
		await AnsiConsole
			.Live(BuildTable())
			.AutoClear(false)
			.StartAsync(async context =>
			{
				using var refreshing = new CancellationTokenSource();
				var pump = PumpAsync(context, refreshing.Token);
				try
				{
					await body().ConfigureAwait(false);
				}
				finally
				{
					await refreshing.CancelAsync().ConfigureAwait(false);
					await pump.ConfigureAwait(false);
					context.UpdateTarget(BuildTable());
				}
			})
			.ConfigureAwait(false);

	/// <summary>Shows a fixture's device as up, with its journeys about to start.</summary>
	/// <param name="config">The fixture that came up.</param>
	public void Ready(PlatformConfig config) => SetCurrent(config, string.Empty);

	/// <summary>Shows a fixture as trying its session again.</summary>
	/// <param name="config">The fixture being retried.</param>
	/// <param name="reason">Why the previous attempt failed.</param>
	public void Retrying(PlatformConfig config, string reason) => SetCurrent(config, $"retrying — {reason}");

	/// <summary>Shows the step a fixture has just finished.</summary>
	/// <param name="config">The fixture the step ran on.</param>
	/// <param name="journeyName">The journey the step belongs to.</param>
	/// <param name="stepNumber">The step's 1-based position.</param>
	/// <param name="totalSteps">How many steps the journey has.</param>
	/// <param name="stepName">The step's bare name.</param>
	public void Step(PlatformConfig config, string journeyName, int stepNumber, int totalSteps, string stepName) =>
		SetCurrent(config, $"{journeyName} {stepNumber}/{totalSteps} {stepName}");

	/// <summary>Counts a finished journey against its fixture's totals.</summary>
	/// <param name="config">The fixture the journey ran on.</param>
	/// <param name="passed">Whether it passed.</param>
	public void JourneyDone(PlatformConfig config, bool passed)
	{
		lock (gate)
		{
			if (rows.TryGetValue(config, out var row))
			{
				row.Done++;
				if (!passed)
				{
					row.Failed++;
				}

				// Left alone between journeys, where the step it last finished is the truest answer to
				// what the fixture is doing.
				if (row.Done == row.Total)
				{
					row.Current = Finished;
				}
			}
		}
	}

	/// <summary>Shows a fixture as abandoned, its remaining journeys never to run.</summary>
	/// <param name="config">The fixture that was abandoned.</param>
	/// <param name="reason">Why it could not run.</param>
	public void Abandoned(PlatformConfig config, string reason)
	{
		lock (gate)
		{
			if (rows.TryGetValue(config, out var row))
			{
				row.Abandoned = true;
				row.Current = reason;
			}
		}
	}

	private void SetCurrent(PlatformConfig config, string text)
	{
		lock (gate)
		{
			if (rows.TryGetValue(config, out var row))
			{
				row.Current = text;
			}
		}
	}

	private async Task PumpAsync(LiveDisplayContext context, CancellationToken cancellationToken)
	{
		while (true)
		{
			try
			{
				await Task.Delay(RefreshIntervalMs, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			context.UpdateTarget(BuildTable());
		}
	}

	private Table BuildTable()
	{
		var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
		_ = table.AddColumn(new TableColumn("Fixture").NoWrap());
		foreach (var header in CountHeaders)
		{
			_ = table.AddColumn(new TableColumn(header).RightAligned().NoWrap());
		}
		_ = table.AddColumn(new TableColumn("Current").Width(CurrentWidth()));

		lock (gate)
		{
			foreach (var row in rows.Values.OrderBy(r => r.Name, StringComparer.Ordinal))
			{
				var left = row.Abandoned ? 0 : row.Total - row.Done;
				_ = table.AddRow(
					new Markup(Markup.Escape(row.Name)),
					Count(row.Total, "grey"),
					Count(row.Done, "green"),
					Count(left, left == 0 ? "grey" : "default"),
					Count(row.Failed, row.Failed == 0 ? "grey" : "red"),
					new Markup($"[grey]{Markup.Escape(row.Current)}[/]").Ellipsis()
				);
			}
		}

		return table;
	}

	private static Markup Count(int value, string colour) =>
		new($"[{colour}]{value.ToString(CultureInfo.InvariantCulture)}[/]");

	/// <summary>
	/// How much of the console the current-step column gets: everything the other columns do not need.
	/// </summary>
	/// <remarks>
	/// Worked out here because a <see cref="Table"/> has no notion of a column that absorbs the slack
	/// — its only levers are <c>Expand</c>, a table width, and per-column width, wrap and alignment.
	/// <c>Expand</c> shares the spare space across every column in proportion to what each is holding,
	/// so a column whose text keeps changing length keeps changing everyone else's width with it, and
	/// the table visibly shifts on each of the several redraws a second. Pinning this one column is
	/// what holds the rest still.
	/// </remarks>
	/// <returns>The width to give the column.</returns>
	private int CurrentWidth()
	{
		// A border between each pair of columns plus the two outer edges, and a space either side of
		// all six columns.
		const int Furniture = 7 + (2 * 6);
		var widest = Math.Max("Fixture".Length, rows.Values.Max(row => row.Name.Length));
		var counts = CountHeaders.Sum(header => header.Length);
		return Math.Max(MinCurrentWidth, AnsiConsole.Profile.Width - Furniture - widest - counts);
	}
}
