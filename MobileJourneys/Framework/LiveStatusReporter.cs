namespace MobileJourneys.Framework;

/// <summary>
/// Reports a run into a <see cref="FixtureStatusTable"/> that redraws in place. Used when the
/// command is run interactively.
/// </summary>
/// <param name="selected">Every journey this run intends to execute, which sets the per-fixture totals.</param>
/// <param name="log">The run's log.</param>
internal sealed class LiveStatusReporter(IReadOnlyList<TestCase> selected, RunLog log) : RunReporter(log)
{
	private readonly FixtureStatusTable table = new(
		selected.GroupBy(testCase => testCase.Config).Select(group => (group.Key, group.Count()))
	);

	/// <inheritdoc/>
	public override Task RunAsync(Func<Task> body) => table.RunAsync(body);

	/// <inheritdoc/>
	protected override void ReportStepCompleted(
		TestStep step,
		int worker,
		int stepNumber,
		int totalSteps,
		string stepName,
		bool passed,
		string? detail
	) => table.Step(step.Config, worker, step.JourneyName, stepNumber, totalSteps, stepName);

	/// <inheritdoc/>
	protected override void ReportJourney(JourneyResult result) =>
		table.JourneyDone(result.TestCase.Config, result.Passed);

	/// <inheritdoc/>
	protected override void ReportFixtureReady(PlatformConfig config, int worker) => table.Ready(config, worker);

	/// <inheritdoc/>
	protected override void ReportFixtureRetrying(PlatformConfig config, int worker, string reason) =>
		table.Retrying(config, worker, reason);

	/// <inheritdoc/>
	protected override void ReportFixtureStartRetrying(PlatformConfig config, string reason) =>
		table.StartRetrying(config, reason);

	/// <inheritdoc/>
	protected override void ReportJourneyStarting(PlatformConfig config, int worker, string journeyName) =>
		table.Preparing(config, worker, journeyName);

	/// <inheritdoc/>
	protected override void ReportWorkerLost(PlatformConfig config, int worker, string reason) =>
		table.Lost(config, worker, reason);

	/// <inheritdoc/>
	/// <remarks>Shown under the table, since writing under a live display corrupts it.</remarks>
	protected override void ReportInterrupted() => table.Stopping();

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
