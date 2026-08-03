using Spectre.Console;
using Spectre.Console.Rendering;

namespace MobileJourneys.Framework;

/// <summary>
/// Lets the reader pick a failed journey after the run and see everything the run knew about it —
/// the step it died on, the explanation, the stack trace, and where its screenshots landed.
/// </summary>
internal static class FailureBrowser
{
	/// <summary>The sentinel index of the "done" entry in the selection list.</summary>
	private const int Done = -1;

	/// <summary>How many failures the selection list shows at once before it scrolls.</summary>
	private const int PageSize = 15;

	/// <summary>
	/// Offers the failures for inspection until the reader is done. Returns immediately when there
	/// are none.
	/// </summary>
	/// <param name="config">The suite's configuration, which locates the failure artifacts.</param>
	/// <param name="failures">The journeys that failed.</param>
	public static void Browse(FrameworkConfig config, IReadOnlyList<JourneyResult> failures)
	{
		if (failures.Count == 0)
		{
			return;
		}

		var ordered = failures.OrderBy(result => result.TestCase.Uid, StringComparer.Ordinal).ToList();
		while (true)
		{
			var choice = AnsiConsole.Prompt(
				new SelectionPrompt<int>()
					.Title($"[red]{ordered.Count}[/] failed. Inspect one? [grey](esc when done)[/]")
					.PageSize(PageSize)
					.MoreChoicesText("[grey](scroll for more)[/]")
					.AddChoices([.. Enumerable.Range(0, ordered.Count), Done])
					.UseConverter(index => index == Done ? "Done" : Markup.Escape(ordered[index].TestCase.DisplayName))
					.AddCancelResult(Done)
			);

			if (choice == Done)
			{
				return;
			}

			Show(config, ordered[choice]);
		}
	}

	private static void Show(FrameworkConfig config, JourneyResult result)
	{
		var rows = new List<IRenderable>();
		if (result.Exception is JourneyFailureException failure)
		{
			rows.Add(
				new Markup($"[bold]Step {failure.StepNumber}/{failure.TotalSteps}[/] {Markup.Escape(failure.StepName)}")
			);
		}

		rows.Add(new Markup(Markup.Escape(result.Explanation)));

		var cause = result.Exception?.InnerException ?? result.Exception;
		if (cause is not null and not JourneyFailureException)
		{
			rows.Add(new Markup($"[red]{Markup.Escape(cause.GetType().FullName ?? cause.GetType().Name)}[/]"));
		}

		rows.Add(
			cause?.StackTrace is { Length: > 0 } stackTrace
				? new Markup($"[grey]{Markup.Escape(stackTrace)}[/]")
				: new Markup("[grey]No stack trace: nothing threw, the screenshot simply differed.[/]")
		);

		var artifacts = config.Storage.FailureArtifactLocations(result.TestCase.Config, result.TestCase.Journey);
		rows.Add(
			artifacts.Count == 0
				? new Markup("[grey]No failure artifacts on disk.[/]")
				: new Rows([.. artifacts.Select(path => new Markup($"[blue]{Markup.Escape(path)}[/]"))])
		);

		AnsiConsole.Write(
			new Panel(new Rows(rows))
			{
				Header = new PanelHeader(Markup.Escape(result.TestCase.DisplayName)),
				Border = BoxBorder.Rounded,
				BorderStyle = new Style(Color.Red),
				Expand = true,
			}
		);
	}
}
