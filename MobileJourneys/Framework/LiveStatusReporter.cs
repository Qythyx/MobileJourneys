namespace MobileJourneys.Framework;

/// <summary>
/// Reports a run into a <see cref="FixtureStatusTable"/> that redraws in place. Used when the
/// command is run interactively.
/// </summary>
/// <param name="selected">Every journey this run intends to execute, which sets the per-fixture totals.</param>
internal sealed class LiveStatusReporter(IReadOnlyList<TestCase> selected) : RunReporter
{
	private readonly FixtureStatusTable table = new(
		selected.GroupBy(testCase => testCase.Config).Select(group => (group.Key, group.Count()))
	);

	/// <inheritdoc/>
	public override Task RunAsync(Func<Task> body) => table.RunAsync(body);

	/// <inheritdoc/>
	public override void StepCompleted(
		TestStep step,
		int stepNumber,
		int totalSteps,
		string stepName,
		bool passed,
		string? detail
	) => table.Step(step.Config, step.JourneyName, stepNumber, totalSteps, stepName);

	/// <inheritdoc/>
	protected override void ReportJourney(JourneyResult result) =>
		table.JourneyDone(result.TestCase.Config, result.Passed);

	/// <inheritdoc/>
	public override void FixtureReady(PlatformConfig config) => table.Ready(config);

	/// <inheritdoc/>
	public override void FixtureRetrying(PlatformConfig config, string reason) => table.Retrying(config, reason);

	/// <inheritdoc/>
	/// <remarks>
	/// The row carries the reason, and nothing is written to the console: fixtures are abandoned
	/// while the table is live now, and writing under a live display corrupts it.
	/// </remarks>
	protected override void ReportFixtureSkipped(PlatformConfig config, int journeyCount, string reason) =>
		table.Abandoned(config, reason);

	/// <inheritdoc/>
	protected override void ReportFailureDetails(IReadOnlyList<JourneyResult> failed)
	{
		// Nothing here: FailureBrowser shows a failure in full when asked for it.
	}
}
