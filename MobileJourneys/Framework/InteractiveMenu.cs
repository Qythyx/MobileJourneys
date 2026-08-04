using Spectre.Console;

namespace MobileJourneys.Framework;

/// <summary>
/// The no-arguments entry point: offers everything the command line can ask for as a menu, and
/// returns the <see cref="RunOptions"/> the reader chose.
/// </summary>
internal static class InteractiveMenu
{
	/// <summary>The group header that selects every journey at once.</summary>
	private const string AllJourneysGroup = "(every journey)";

	/// <summary>How many journeys the multi-select shows before it starts scrolling.</summary>
	private const int JourneyPageSize = 20;

	private enum Choice
	{
		RunAll,
		RunSome,
		RerunFailed,
		Review,
		ListExtraneous,
		DeleteExtraneous,
		Quit,
	}

	/// <summary>Asks what to do, re-asking whenever the reader backs out of a deeper prompt.</summary>
	/// <param name="config">The suite, for its display name and the journeys to offer.</param>
	/// <returns>Options equivalent to the command line the reader would otherwise have typed.</returns>
	public static RunOptions Choose(FrameworkConfig config)
	{
		AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(config.DisplayName)}[/]") { Justification = Justify.Left });
		while (true)
		{
			var choice = AnsiConsole.Prompt(
				new SelectionPrompt<Choice>()
					.Title("What would you like to do? [grey](esc to quit)[/]")
					.AddChoices(Enum.GetValues<Choice>())
					.UseConverter(Describe)
					.AddCancelResult(Choice.Quit)
			);

			if (ToOptions(choice, config) is { } options)
			{
				return options;
			}
		}
	}

	/// <summary>Turns a menu choice into options, or <c>null</c> when its own prompt was backed out of.</summary>
	/// <param name="choice">What the reader picked.</param>
	/// <param name="config">The suite, for its journeys.</param>
	/// <returns>The options, or <c>null</c> to ask again.</returns>
	private static RunOptions? ToOptions(Choice choice, FrameworkConfig config) =>
		choice switch
		{
			Choice.RunAll => new RunOptions(RunMode.Run, [], [], false, null, null),
			Choice.RunSome => PromptForSelection(config),
			Choice.RerunFailed => new RunOptions(RunMode.Run, [], [], true, null, null),
			Choice.Review => new RunOptions(RunMode.Review, [], [], false, null, null),
			Choice.ListExtraneous => new RunOptions(RunMode.ListExtraneous, [], [], false, null, null),
			Choice.DeleteExtraneous => new RunOptions(RunMode.DeleteExtraneous, [], [], false, null, null),
			Choice.Quit => new RunOptions(RunMode.Quit, [], [], false, null, null),
			_ => throw new ArgumentOutOfRangeException(nameof(choice)),
		};

	private static string Describe(Choice choice) =>
		choice switch
		{
			Choice.RunAll => "Run every journey on every fixture",
			Choice.RunSome => "Pick journeys to run",
			Choice.RerunFailed => "Rerun the journeys that have failure artifacts on disk",
			Choice.Review => "Review screenshots in the browser",
			Choice.ListExtraneous => "List screenshots no journey references",
			Choice.DeleteExtraneous => "Delete screenshots no journey references",
			Choice.Quit => "Quit",
			_ => throw new ArgumentOutOfRangeException(nameof(choice)),
		};

	/// <summary>
	/// Asks which journeys to run, then for any extra filters. Sequenced in a body because prompting
	/// inline would run the two in constructor-argument order, which asks for the filter first.
	/// </summary>
	/// <param name="config">The suite, for its journeys.</param>
	/// <returns>A run of the chosen journeys narrowed by the filters typed, or <c>null</c> if
	/// the journey list was backed out of.</returns>
	private static RunOptions? PromptForSelection(FrameworkConfig config)
	{
		var journeys = PromptForJourneys(config);
		return journeys.Count == 0
			? null
			: new RunOptions(RunMode.Run, PromptForFilters(), journeys, false, null, null);
	}

	/// <summary>Offers every journey by name, with a group header that takes the lot.</summary>
	/// <param name="config">The suite, for its journeys.</param>
	/// <returns>The chosen journey names, or an empty list when the reader pressed escape. The
	/// prompt requires a selection to accept, so empty can only mean backing out.</returns>
	private static IReadOnlyList<string> PromptForJourneys(FrameworkConfig config)
	{
		var names = config.Journeys.Select(journey => journey.Name).ToList();
		var chosen = AnsiConsole.Prompt(
			new MultiSelectionPrompt<string>()
				.Title("Journeys [grey](space to toggle, enter to accept, esc to go back)[/]:")
				.PageSize(JourneyPageSize)
				.MoreChoicesText("[grey](move up and down for more)[/]")
				.AddChoiceGroup(AllJourneysGroup, names)
				.AddCancelResult()
		);
		// The group header comes back alongside its children when it is the thing that was toggled.
		return [.. chosen.Where(name => name != AllJourneysGroup)];
	}

	private static IReadOnlyList<string> PromptForFilters()
	{
		var text = AnsiConsole.Prompt(
			new TextPrompt<string>(
				"Filter [grey](optional; words separated by spaces, and a journey must match every one)[/]:"
			).AllowEmpty()
		);
		return [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
	}
}
