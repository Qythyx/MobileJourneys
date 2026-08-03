using Spectre.Console;

namespace MobileJourneys.Framework;

/// <summary>
/// Collects what a run produced and renders it. Subclasses differ only in how progress reaches the
/// screen — <see cref="ConsoleReporter"/> writes a line per step, <see cref="LiveStatusReporter"/>
/// redraws a per-fixture table in place. The tally, the failure recording, and the end-of-run
/// verdict are the same either way and live here.
/// </summary>
/// <remarks>
/// The platform fixtures run concurrently and report from different threads, so every write takes
/// <see cref="Gate"/> — a line assembled from several coloured parts would otherwise interleave
/// with another fixture's.
/// </remarks>
internal abstract class RunReporter
{
	/// <summary>Serializes console writes and the shared tally across the fixture threads.</summary>
	protected static readonly Lock Gate = new();

	/// <summary>
	/// Room to write into when nothing is there to measure. A redirected console has no width of its
	/// own, so Spectre assumes a narrow one and hard-wraps to it — which would fold these lines in
	/// the middle of a word. The review server streams them into its page and CI files them into a
	/// log, and both want them whole.
	/// </summary>
	private const int RedirectedWidth = 4096;

	static RunReporter()
	{
		if (Console.IsOutputRedirected)
		{
			AnsiConsole.Profile.Width = RedirectedWidth;
		}
	}

	private readonly List<JourneyResult> results = [];

	private int skippedFixtures;

	/// <summary>Prints the header naming the suite and how much of it is about to run.</summary>
	/// <param name="displayName">The suite's display name.</param>
	/// <param name="selected">How many journeys were selected.</param>
	/// <param name="total">How many journeys exist across all fixtures.</param>
	public static void Header(string displayName, int selected, int total)
	{
		var scope = selected == total ? $"{total} journeys" : $"{selected} of {total} journeys";
		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine(
			$"{Markup.Escape(displayName)} — {scope} [dim](MobileJourneys {Markup.Escape(TestAssembly.FrameworkVersion)})[/]"
		);
		AnsiConsole.WriteLine();
	}

	/// <summary>Prints a note that does not bear on the run's outcome.</summary>
	/// <param name="text">The note.</param>
	public static void Note(string text)
	{
		lock (Gate)
		{
			AnsiConsole.MarkupLine($"[yellow]NOTE[/] {Markup.Escape(text)}");
		}
	}

	/// <summary>The journeys that failed, in the order they finished.</summary>
	public IReadOnlyList<JourneyResult> Failures
	{
		get
		{
			lock (Gate)
			{
				return [.. results.Where(r => !r.Passed)];
			}
		}
	}

	/// <summary>Reports a step that has finished.</summary>
	/// <param name="testCase">The journey and fixture the step belongs to.</param>
	/// <param name="stepNumber">The step's 1-based position.</param>
	/// <param name="totalSteps">How many steps the journey has.</param>
	/// <param name="stepName">The step's name.</param>
	/// <param name="passed">Whether the step's screenshot matched its baseline.</param>
	/// <param name="detail">What went wrong, or <c>null</c> when the step passed.</param>
	public abstract void StepCompleted(
		TestCase testCase,
		int stepNumber,
		int totalSteps,
		string stepName,
		bool passed,
		string? detail
	);

	/// <summary>Records a finished journey and hands it to the subclass to render.</summary>
	/// <param name="result">The journey's outcome.</param>
	public void JourneyCompleted(JourneyResult result)
	{
		if (!result.Passed)
		{
			FailureSummary.RecordFailure(result.TestCase);
		}

		lock (Gate)
		{
			results.Add(result);
		}

		ReportJourney(result);
	}

	/// <summary>Renders a finished journey.</summary>
	/// <param name="result">The journey's outcome, already added to the tally.</param>
	protected abstract void ReportJourney(JourneyResult result);

	/// <summary>
	/// Reports a whole fixture that could not be run — the app failed to start on its device, say.
	/// This fails the run: its journeys produce no results, so without this the summary would see
	/// nothing wrong and report success for a run that tested nothing.
	/// </summary>
	/// <param name="config">The fixture that was abandoned.</param>
	/// <param name="journeyCount">How many journeys it would have run.</param>
	/// <param name="reason">Why it could not run, ready to print.</param>
	public void FixtureSkipped(PlatformConfig config, int journeyCount, string reason)
	{
		lock (Gate)
		{
			skippedFixtures++;
		}

		ReportFixtureSkipped(config, journeyCount, reason);
	}

	/// <summary>Renders an abandoned fixture.</summary>
	/// <param name="config">The fixture that was abandoned.</param>
	/// <param name="journeyCount">How many journeys it would have run.</param>
	/// <param name="reason">Why it could not run, ready to print.</param>
	protected abstract void ReportFixtureSkipped(PlatformConfig config, int journeyCount, string reason);

	/// <summary>
	/// Notes that a fixture's device is up and its journeys are about to start. Fixtures now start
	/// inside the display rather than before it, so without this a row would still read as starting
	/// until its first step finished — most of a minute later.
	/// </summary>
	/// <param name="config">The fixture that came up.</param>
	public virtual void FixtureReady(PlatformConfig config) { }

	/// <summary>
	/// Runs the fixture fan-out inside whatever display this reporter keeps on screen for its
	/// duration.
	/// </summary>
	/// <param name="body">The fan-out to run.</param>
	/// <returns>A task completing when the fan-out has finished and the display has been torn down.</returns>
	public virtual Task RunAsync(Func<Task> body) => body();

	/// <summary>
	/// Prints the end-of-run summary: each failure's explanation, then the banner indexing the
	/// failed journeys by fixture, then the verdict as the last line.
	/// </summary>
	/// <returns>The process exit code — 0 when everything passed.</returns>
	public int Summarize()
	{
		var failed = Failures;
		ReportFailureDetails(failed);
		FailureSummary.Print(Console.Out);
		var journeys = results.Count == 1 ? "journey" : "journeys";
		var abandoned =
			skippedFixtures == 0 ? ""
			: skippedFixtures == 1 ? " 1 fixture was abandoned."
			: $" {skippedFixtures} fixtures were abandoned.";
		AnsiConsole.MarkupLine(
			failed.Count == 0 && skippedFixtures == 0
				? $"[green]All {results.Count} {journeys} passed.[/]"
				: $"[red]{failed.Count} of {results.Count} {journeys} failed.{abandoned}[/]"
		);
		return failed.Count == 0 && skippedFixtures == 0 ? 0 : 1;
	}

	/// <summary>
	/// Prints every failure's explanation in full. Overridden to print nothing where the failures
	/// are browsable on demand instead.
	/// </summary>
	/// <param name="failed">The failed journeys.</param>
	protected virtual void ReportFailureDetails(IReadOnlyList<JourneyResult> failed)
	{
		AnsiConsole.WriteLine();
		foreach (var result in failed.OrderBy(r => r.TestCase.Uid, StringComparer.Ordinal))
		{
			AnsiConsole.MarkupLine($"[red]FAIL[/] {Markup.Escape(result.TestCase.Uid)}");
			AnsiConsole.WriteLine(Indent(result.Explanation));
		}
	}

	/// <summary>Names a fixture the short way — device and theme, without the platform and version.</summary>
	/// <param name="config">The fixture to name.</param>
	/// <returns>The label, e.g. <c>iPhone 17 Pro light</c>.</returns>
	internal static string FixtureLabel(PlatformConfig config) =>
		$"{config.DeviceName} {(config.IsLightTheme ? "light" : "dark")}";

	/// <summary>Indents every line of a block of text by four spaces, trimming trailing space.</summary>
	/// <param name="text">The text to indent.</param>
	/// <returns>The indented text.</returns>
	protected static string Indent(string text) =>
		string.Join('\n', text.Split('\n').Select(line => "    " + line.TrimEnd()));
}
