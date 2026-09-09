using System.Globalization;
using Spectre.Console;

namespace MobileJourneys.Framework;

/// <summary>
/// Renders a run as sequential lines — one per finished step, one per finished journey. Used
/// when the command is not run interactively.
/// </summary>
internal sealed class ConsoleReporter : RunReporter
{
	/// <summary>Width the fixture tag is padded to, so journey names line up across fixtures.</summary>
	private const int TagWidth = 20;

	/// <inheritdoc/>
	public override void StepCompleted(
		TestStep step,
		int worker,
		int stepNumber,
		int totalSteps,
		string stepName,
		bool passed,
		string? detail
	)
	{
		var mark = passed ? "[green]✓[/]" : "[red]✗[/]";
		var name = Markup.Escape(step.JourneyName).PadRight(18);
		var line =
			$"  {mark} [dim]{Markup.Escape(Tag(StepLabel(step.Config, worker)))}[/] {name} [dim]{stepNumber}/{totalSteps}[/]  {Markup.Escape(stepName)}";
		lock (Gate)
		{
			AnsiConsole.MarkupLine(passed || detail is null ? line : $"{line} — [red]{Markup.Escape(detail)}[/]");
		}
	}

	/// <inheritdoc/>
	protected override void ReportJourney(JourneyResult result)
	{
		var mark = result.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
		var seconds = result.Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
		var name = Markup.Escape(result.TestCase.Journey.Name).PadRight(18);
		lock (Gate)
		{
			AnsiConsole.MarkupLine(
				$"{mark} {Markup.Escape(Tag(FixtureLabel(result.TestCase.Config)))} {name} [dim]{seconds}s[/]"
			);
		}
	}

	/// <inheritdoc/>
	public override void FixtureRetrying(PlatformConfig config, int worker, string reason)
	{
		lock (Gate)
		{
			AnsiConsole.MarkupLine($"[yellow]RETRY[/] {Markup.Escape($"{WorkerLabel(config, worker)} — {reason}")}");
		}
	}

	/// <inheritdoc/>
	public override void WorkerLost(PlatformConfig config, int worker, string reason)
	{
		lock (Gate)
		{
			AnsiConsole.MarkupLine($"[yellow]LOST[/] {Markup.Escape($"{WorkerLabel(config, worker)} — {reason}")}");
		}
	}

	/// <inheritdoc/>
	protected override void ReportFixtureSkipped(PlatformConfig config, int journeyCount, string reason)
	{
		lock (Gate)
		{
			AnsiConsole.MarkupLine(
				$"[yellow]SKIP[/] {Markup.Escape($"{config} — skipped {journeyCount} journeys because {reason}")}"
			);
		}
	}

	private static string Tag(string label) => label.PadRight(TagWidth);

	/// <summary>Names a worker the short way, for a step line's tag.</summary>
	private static string StepLabel(PlatformConfig config, int worker) =>
		config.Instances == 1 ? FixtureLabel(config) : $"{FixtureLabel(config)} #{worker}";

	/// <summary>Names a worker the long way, for lines that stand on their own.</summary>
	private static string WorkerLabel(PlatformConfig config, int worker) =>
		config.Instances == 1 ? config.ToString() : $"{config} worker {worker}";
}
