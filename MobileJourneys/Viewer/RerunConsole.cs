using System.Text.Json;
using MobileJourneys.Framework;
using Spectre.Console;

namespace MobileJourneys.Viewer;

/// <summary>
/// Shows a rerun's progress on the review server's own console. A rerun runs in a child process
/// whose output is a pipe and which therefore reports over HTTP, having no terminal to draw a table
/// on; the table is built here instead, from the same events the page is driven by.
/// </summary>
/// <remarks>
/// There is no table until the run declares its fixture totals, which is a build away from the
/// child starting. The child's output shows until then, and goes on showing for a rerun that never
/// reported at all — one that failed to build.
/// </remarks>
/// <param name="config">The suite, used to resolve the fixture an event names.</param>
internal sealed class RerunConsole(FrameworkConfig config)
{
	/// <summary>How long to let the live display finish its last redraw before writing under it.</summary>
	private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

	/// <summary>Completed when the child exits, which is what brings the table down.</summary>
	private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Guards the two fields the display is started and awaited through.</summary>
	private readonly Lock gate = new();

	private FixtureStatusTable? table;

	private Task? display;

	/// <summary>Announces the rerun that is starting.</summary>
	/// <param name="description">What is being rerun, as the page describes it.</param>
	internal static void Announce(string description)
	{
		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine($"[cyan]Rerunning[/] {Markup.Escape(description)}");
	}

	/// <summary>
	/// Writes one line of the child's output, while there is no table to disturb.
	/// </summary>
	/// <param name="text">The line, as the child wrote it.</param>
	internal void Line(string text)
	{
		lock (gate)
		{
			if (table is null)
			{
				// Not through Spectre: the child has already rendered this line, and a second pass
				// would read its brackets as markup and re-wrap its paths at the profile width.
				Console.WriteLine(text);
			}
		}
	}

	/// <summary>
	/// Applies one reported event to the table, ignoring the ones it does not draw. Called on the
	/// thread that received the event, in the order the run produced them.
	/// </summary>
	/// <param name="json">The event as the run serialized it.</param>
	internal void Consume(string json)
	{
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		var type = Text(root, "type");
		if (type == "run-started")
		{
			Start(root);
			return;
		}

		FixtureStatusTable? current;
		lock (gate)
		{
			current = table;
		}

		if (current is null || Fixture(root) is not { } fixture)
		{
			return;
		}

		switch (type)
		{
			case "fixture-ready":
				current.Ready(fixture);
				return;
			case "fixture-retrying":
				current.Retrying(fixture, Text(root, "reason") ?? string.Empty);
				return;
			case "fixture-skipped":
				current.Abandoned(fixture, Text(root, "reason") ?? string.Empty);
				return;
			case "step-completed":
				current.Step(
					fixture,
					Text(root, "journey") ?? string.Empty,
					Number(root, "number"),
					Number(root, "totalSteps"),
					Text(root, "name") ?? string.Empty
				);
				return;
			case "journey-completed":
				current.JourneyDone(fixture, root.TryGetProperty("passed", out var passed) && passed.GetBoolean());
				return;
			default:
				return;
		}
	}

	/// <summary>
	/// Brings the table down and states how the rerun ended. Blocks briefly while the display
	/// finishes, so the verdict is not written into a table still redrawing itself.
	/// </summary>
	/// <param name="exitCode">The exit code <c>dotnet run</c> returned.</param>
	internal void Finish(int exitCode)
	{
		_ = finished.TrySetResult();
		Task? pending;
		lock (gate)
		{
			pending = display;
		}

		_ = pending?.Wait(TeardownTimeout);
		AnsiConsole.MarkupLine(
			exitCode == 0 ? "[green]Rerun finished.[/]" : $"[red]Rerun finished with exit code {exitCode}.[/]"
		);
		AnsiConsole.WriteLine();
	}

	/// <summary>
	/// Puts the table up, sized by the totals the run declares as it starts. Every later event needs
	/// it, so a run whose fixtures all resolve to nothing leaves the child's output showing instead.
	/// </summary>
	/// <param name="root">The <c>run-started</c> event.</param>
	private void Start(JsonElement root)
	{
		// Without a terminal there is nothing to redraw into, and the child's own output — which keeps
		// flowing while no table is up — is the better record anyway.
		if (!SuiteRunner.IsInteractive || !root.TryGetProperty("fixtures", out var declared))
		{
			return;
		}

		var seeded = declared
			.EnumerateArray()
			.Select(fixture =>
				(Config: config.FindPlatform(Text(fixture, "config")), Total: Number(fixture, "journeys"))
			)
			.Where(fixture => fixture.Config is not null)
			.Select(fixture => (fixture.Config!, fixture.Total))
			.ToList();
		if (seeded.Count == 0)
		{
			return;
		}

		var started = new FixtureStatusTable(seeded);
		lock (gate)
		{
			table = started;
			display = Task.Run(async () =>
			{
				try
				{
					await started.RunAsync(() => finished.Task).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					RunReporter.Note($"the rerun's console display stopped: {ex.Message}");
				}
			});
		}
	}

	private PlatformConfig? Fixture(JsonElement root) => config.FindPlatform(Text(root, "config"));

	private static string? Text(JsonElement root, string name) =>
		root.TryGetProperty(name, out var value) ? value.GetString() : null;

	private static int Number(JsonElement root, string name) =>
		root.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
}
