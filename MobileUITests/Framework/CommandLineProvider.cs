using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.CommandLine;

namespace MobileUITests.Framework;

internal sealed class CommandLineProvider(FrameworkConfig config) : ICommandLineOptionsProvider
{
	public const string FilterOption = "filter";
	public const string RerunOption = "rerun";
	public const string ListExtraneousOption = "list-extraneous";
	public const string DeleteExtraneousOption = "delete-extraneous";

	public string Uid => $"{TestAssembly.Name}.CommandLine";

	public string Version => "1.0.0";

	public string DisplayName => $"{config.DisplayName} command-line options";

	public string Description => "Selection and extraneous-screenshot maintenance flags.";

	public Task<bool> IsEnabledAsync() => Task.FromResult(true);

	public IReadOnlyCollection<CommandLineOption> GetCommandLineOptions() =>
		[
			new(
				FilterOption,
				"Case-insensitive substring match against '{PlatformConfig}.{JourneyName}'. Repeat to union.",
				ArgumentArity.OneOrMore,
				isHidden: false
			),
			new(
				RerunOption,
				"Restrict the run to journeys with failure artifacts on disk (combine with --filter to narrow).",
				ArgumentArity.Zero,
				isHidden: false
			),
			new(
				ListExtraneousOption,
				"List screenshot files/folders not referenced by any current journey, then exit without running tests.",
				ArgumentArity.Zero,
				isHidden: false
			),
			new(
				DeleteExtraneousOption,
				"Delete screenshot files/folders not referenced by any current journey, then exit without running tests.",
				ArgumentArity.Zero,
				isHidden: false
			),
		];

	public Task<ValidationResult> ValidateOptionArgumentsAsync(CommandLineOption commandOption, string[] arguments) =>
		ValidationResult.ValidTask;

	public Task<ValidationResult> ValidateCommandLineOptionsAsync(ICommandLineOptions commandLineOptions) =>
		ValidationResult.ValidTask;
}
