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
			$"  {mark} [dim]{Markup.Escape(Tag(step.Config))}[/] {name} [dim]{stepNumber}/{totalSteps}[/]  {Markup.Escape(stepName)}";
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
			AnsiConsole.MarkupLine($"{mark} {Markup.Escape(Tag(result.TestCase.Config))} {name} [dim]{seconds}s[/]");
		}
	}

	/// <inheritdoc/>
	public override void FixtureRetrying(PlatformConfig config, string reason)
	{
		lock (Gate)
		{
			AnsiConsole.MarkupLine($"[yellow]RETRY[/] {Markup.Escape($"{config} — {reason}")}");
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

	private static string Tag(PlatformConfig config) => FixtureLabel(config).PadRight(TagWidth);
}
