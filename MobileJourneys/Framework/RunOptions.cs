namespace MobileJourneys.Framework;

/// <summary>What the runner was asked to do. Everything but <see cref="Run"/> exits without a session.</summary>
public enum RunMode
{
	/// <summary>Run the selected journeys.</summary>
	Run,

	/// <summary>Ask the reader what to do, then do that.</summary>
	Interactive,

	/// <summary>List screenshots no journey references.</summary>
	ListExtraneous,

	/// <summary>Delete screenshots no journey references.</summary>
	DeleteExtraneous,

	/// <summary>Serve the screenshot viewer with its review actions enabled.</summary>
	Review,

	/// <summary>Print usage.</summary>
	Help,

	/// <summary>Do nothing and exit successfully.</summary>
	Quit,
}

/// <summary>
/// The runner's command line, parsed. Replaces the Microsoft.Testing.Platform options provider:
/// the runner owns its own process, so the flags are read here rather than declared to a host.
/// </summary>
/// <param name="Mode">What to do.</param>
/// <param name="Filters">Substrings a journey's <c>{PlatformConfig}.{JourneyName}</c> must all contain.</param>
/// <param name="JourneyNames">Journey names, matched whole and case-insensitively; a journey need only be one
/// of them. Empty means every journey.</param>
/// <param name="Rerun">Whether to restrict the run to journeys with failure artifacts on disk.</param>
/// <param name="ReportTo">URL to POST run events to, or <c>null</c> to report to the console instead.</param>
/// <param name="Error">The parse error to report, or <c>null</c> when the command line was valid.</param>
public sealed record RunOptions(
	RunMode Mode,
	IReadOnlyList<string> Filters,
	IReadOnlyList<string> JourneyNames,
	bool Rerun,
	string? ReportTo,
	string? Error
)
{
	/// <summary>Parses the runner's arguments, never throwing — a bad command line becomes <see cref="Error"/>.</summary>
	/// <param name="args">The arguments as passed to <c>Main</c>.</param>
	/// <returns>The parsed options.</returns>
	public static RunOptions Parse(string[] args)
	{
		var filters = new List<string>();
		var journeyNames = new List<string>();
		RunMode? mode = null;
		var rerun = false;
		string? reportTo = null;

		// An editor that substitutes an unset filter into an argument array leaves a blank element
		// behind rather than dropping it, and a blank is never a real argument or a useful filter
		// (every Uid contains the empty string).
		args = [.. args.Where(a => !string.IsNullOrWhiteSpace(a))];

		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--run":
					mode = RunMode.Run;
					break;

				case "--filter":
					if (!TakeValues(args, ref i, filters))
					{
						return Invalid("--filter needs at least one value.");
					}
					break;

				case "--journey":
					if (!TakeValues(args, ref i, journeyNames))
					{
						return Invalid("--journey needs at least one value.");
					}
					break;

				case "--rerun":
					rerun = true;
					break;

				case "--report-to":
					if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
					{
						return Invalid("--report-to needs a URL.");
					}
					reportTo = args[++i];
					break;

				case "--list-extraneous":
					mode = RunMode.ListExtraneous;
					break;

				case "--delete-extraneous":
					mode = RunMode.DeleteExtraneous;
					break;

				case "--review":
					mode = RunMode.Review;
					break;

				case "--help"
				or "-h":
					mode = RunMode.Help;
					break;

				default:
					return Invalid($"Unrecognized argument '{args[i]}'.");
			}
		}

		if (mode is null)
		{
			// A run occupies every configured device for as long as it takes, so it has to be asked
			// for by name — a narrowing flag on its own is not a request to run.
			return args.Length == 0
				? new RunOptions(RunMode.Interactive, [], [], false, null, null)
				: Invalid("Add --run to run journeys, or pass no arguments at all to choose from a menu.");
		}

		return new RunOptions(mode.Value, filters, journeyNames, rerun, reportTo, null);

		static RunOptions Invalid(string error) => new(RunMode.Help, [], [], false, null, error);

		// Both repeated flags and several values after one flag, since both read naturally at a call
		// site. False when the flag was given nothing to collect.
		static bool TakeValues(string[] args, ref int index, List<string> values)
		{
			var start = index;
			while (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
			{
				values.Add(args[++index]);
			}
			return index > start;
		}
	}

	/// <summary>The usage text, printed for <c>--help</c> and after a parse error.</summary>
	/// <param name="displayName">The suite's display name, used in the header line.</param>
	/// <returns>The usage text.</returns>
	public static string Usage(string displayName) =>
		$$"""
			{{displayName}}

			Usage: dotnet run --project <test project> -- [options]

			  With no options at all, and a terminal to ask in, the runner offers the same choices
			  as a menu.

			  --run                 Run the selected journeys. Required — no other flag starts a run.
			  --journey <name>...   Whole journey name, case-insensitive. Repeatable; a journey need
			                        only match one of them.
			  --filter <text>...    Case-insensitive substring match against '{PlatformConfig}.{JourneyName}'.
			                        Repeatable; every filter must match.
			  --rerun               Restrict the run to journeys with failure artifacts on disk.
			  --report-to <url>     POST run events to this URL instead of writing progress to the
			                        console. Set by the review server when it launches a rerun.
			  --list-extraneous     List screenshots no journey references, then exit.
			  --delete-extraneous   Delete screenshots no journey references, then exit.
			  --review              Serve the screenshot viewer with review actions, instead of running.
			  --help, -h            Show this help.
			""";
}
