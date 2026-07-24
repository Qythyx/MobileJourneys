using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.CommandLine;

namespace MobileJourneys.Framework;

/// <summary>
/// Adds the framework's CLI flags (<c>--filter</c>, <c>--rerun</c>, <c>--list-extraneous</c>,
/// <c>--delete-extraneous</c>) to the Microsoft.Testing.Platform host. The consumer's
/// <c>Program.Main</c> registers an instance via <c>builder.CommandLine.AddProvider</c>.
/// </summary>
/// <param name="config">The framework configuration; supplies <see cref="DisplayName"/>.</param>
public sealed class CommandLineProvider(FrameworkConfig config) : ICommandLineOptionsProvider
{
	/// <summary>The CLI flag name for filtering tests by substring (without the leading <c>--</c>).</summary>
	public const string FilterOption = "filter";

	/// <summary>The CLI flag name for rerunning only journeys with failure artifacts.</summary>
	public const string RerunOption = "rerun";

	/// <summary>The CLI flag name for listing orphaned screenshot baselines.</summary>
	public const string ListExtraneousOption = "list-extraneous";

	/// <summary>The CLI flag name for deleting orphaned screenshot baselines.</summary>
	public const string DeleteExtraneousOption = "delete-extraneous";

	/// <summary>The CLI flag name for serving the screenshot viewer with review actions enabled.</summary>
	public const string ReviewOption = "review";

	/// <inheritdoc/>
	public string Uid => $"{TestAssembly.Name}.CommandLine";

	/// <inheritdoc/>
	public string Version => TestAssembly.FrameworkVersion;

	/// <inheritdoc/>
	public string DisplayName => $"{config.DisplayName} command-line options";

	/// <inheritdoc/>
	public string Description => "Selection and extraneous-screenshot maintenance flags.";

	/// <inheritdoc/>
	public Task<bool> IsEnabledAsync() => Task.FromResult(true);

	/// <inheritdoc/>
	public IReadOnlyCollection<CommandLineOption> GetCommandLineOptions() =>
		[
			new(
				FilterOption,
				"Case-insensitive substring match against '{PlatformConfig}.{JourneyName}'. Multiple filters must all match.",
				ArgumentArity.OneOrMore,
				isHidden: false
			),
			new(
				RerunOption,
				$"Restrict the run to journeys with failure artifacts on disk (combine with --{FilterOption} to narrow).",
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
			new(
				ReviewOption,
				"Serve the screenshot viewer on a local port with Accept/Reject/Delete actions enabled, instead of running tests.",
				ArgumentArity.Zero,
				isHidden: false
			),
		];

	/// <inheritdoc/>
	public Task<ValidationResult> ValidateOptionArgumentsAsync(CommandLineOption commandOption, string[] arguments) =>
		ValidationResult.ValidTask;

	/// <inheritdoc/>
	public Task<ValidationResult> ValidateCommandLineOptionsAsync(ICommandLineOptions commandLineOptions) =>
		ValidationResult.ValidTask;
}
