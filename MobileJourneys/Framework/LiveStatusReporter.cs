using System.Globalization;
using Spectre.Console;

namespace MobileJourneys.Framework;

/// <summary>
/// Renders a run as a table that redraws in place: one row per platform fixture, showing how many
/// of its journeys are done, still to come, and failed, plus the step it is on right now. Used
/// when the command is run interactively.
/// </summary>
/// <param name="selected">Every journey this run intends to execute, which sets the per-fixture totals.</param>
internal sealed class LiveStatusReporter(IReadOnlyList<TestCase> selected) : RunReporter
{
	private const int RefreshIntervalMs = 150;

	/// <summary>Narrowest the current-step column is allowed to get on a cramped terminal.</summary>
	private const int MinCurrentWidth = 20;

	/// <summary>What a fixture's row reads once it has no journeys left to run.</summary>
	private const string Finished = "all journeys completed";

	/// <summary>The count columns, which are never wider than their headers.</summary>
	private static readonly string[] CountHeaders = ["Total", "Done", "Left", "Fail"];

	private readonly Dictionary<PlatformConfig, FixtureProgress> fixtures = selected
		.GroupBy(testCase => testCase.Config)
		.ToDictionary(group => group.Key, group => new FixtureProgress(FixtureLabel(group.Key), group.Count()));

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

	/// <inheritdoc/>
	public override async Task RunAsync(Func<Task> body) =>
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

	/// <inheritdoc/>
	public override void StepCompleted(
		TestStep step,
		int stepNumber,
		int totalSteps,
		string stepName,
		bool passed,
		string? detail
	)
	{
		lock (Gate)
		{
			if (fixtures.TryGetValue(step.Config, out var fixture))
			{
				fixture.Current = $"{step.JourneyName} {stepNumber}/{totalSteps} {stepName}";
			}
		}
	}

	/// <inheritdoc/>
	protected override void ReportJourney(JourneyResult result)
	{
		lock (Gate)
		{
			if (fixtures.TryGetValue(result.TestCase.Config, out var fixture))
			{
				fixture.Done++;
				if (!result.Passed)
				{
					fixture.Failed++;
				}

				// Left alone between journeys, where the step it last finished is the truest answer to
				// what the fixture is doing.
				if (fixture.Done == fixture.Total)
				{
					fixture.Current = Finished;
				}
			}
		}
	}

	/// <inheritdoc/>
	public override void FixtureReady(PlatformConfig config)
	{
		lock (Gate)
		{
			if (fixtures.TryGetValue(config, out var fixture))
			{
				fixture.Current = string.Empty;
			}
		}
	}

	/// <inheritdoc/>
	public override void FixtureRetrying(PlatformConfig config, string reason)
	{
		lock (Gate)
		{
			if (fixtures.TryGetValue(config, out var fixture))
			{
				fixture.Current = $"retrying — {reason}";
			}
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The row carries the reason, and nothing is written to the console: fixtures are abandoned
	/// while the table is live now, and writing under a live display corrupts it.
	/// </remarks>
	protected override void ReportFixtureSkipped(PlatformConfig config, int journeyCount, string reason)
	{
		lock (Gate)
		{
			if (fixtures.TryGetValue(config, out var fixture))
			{
				fixture.Abandoned = true;
				fixture.Current = reason;
			}
		}
	}

	/// <inheritdoc/>
	protected override void ReportFailureDetails(IReadOnlyList<JourneyResult> failed)
	{
		// Nothing here: FailureBrowser shows a failure in full when asked for it.
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

		lock (Gate)
		{
			foreach (var fixture in fixtures.Values.OrderBy(f => f.Name, StringComparer.Ordinal))
			{
				var left = fixture.Abandoned ? 0 : fixture.Total - fixture.Done;
				_ = table.AddRow(
					new Markup(Markup.Escape(fixture.Name)),
					Count(fixture.Total, "grey"),
					Count(fixture.Done, "green"),
					Count(left, left == 0 ? "grey" : "default"),
					Count(fixture.Failed, fixture.Failed == 0 ? "grey" : "red"),
					new Markup($"[grey]{Markup.Escape(fixture.Current)}[/]").Ellipsis()
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
		var widest = Math.Max("Fixture".Length, fixtures.Values.Max(fixture => fixture.Name.Length));
		var counts = CountHeaders.Sum(header => header.Length);
		return Math.Max(MinCurrentWidth, AnsiConsole.Profile.Width - Furniture - widest - counts);
	}
}
