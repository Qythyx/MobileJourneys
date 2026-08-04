using System.Text;
using System.Text.Json;

namespace MobileJourneys.Framework;

/// <summary>
/// Reports a run to the process that launched it, as one JSON object POSTed per thing that happens
/// to the URL <c>--report-to</c> named. The review server passes its own endpoint there and relays
/// each event to the open page, which is how a rerun's progress reaches the browser.
/// </summary>
/// <remarks>
/// Every event is posted synchronously under <see cref="postGate"/>, and the server records an
/// event before answering the request, so the order the run produced them in is the order the
/// listener sees — which matters because the first event declares the totals the rest count against.
/// </remarks>
/// <param name="url">Where to POST each event.</param>
/// <param name="selected">Every journey this run intends to execute, which sets the per-fixture totals.</param>
internal sealed class WebReporter(string url, IReadOnlyList<TestCase> selected) : RunReporter
{
	private const int PostTimeoutSeconds = 10;

	private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(PostTimeoutSeconds) };

	private static readonly JsonSerializerOptions SerializerOptions = new();

	private readonly Lock postGate = new();

	/// <inheritdoc/>
	public override Task RunAsync(Func<Task> body)
	{
		Post(
			new
			{
				type = "run-started",
				fixtures = selected
					.GroupBy(testCase => testCase.Config)
					.Select(group => new
					{
						config = group.Key.DisplayName,
						journeys = group.Count(),
						steps = group.Sum(testCase => testCase.Journey.ExpectedStepLocations().Count()),
					}),
			}
		);
		return body();
	}

	/// <inheritdoc/>
	public override void FixtureReady(PlatformConfig config) =>
		Post(new { type = "fixture-ready", config = config.DisplayName });

	/// <inheritdoc/>
	protected override void ReportFixtureSkipped(PlatformConfig config, int journeyCount, string reason) =>
		Post(
			new
			{
				type = "fixture-skipped",
				config = config.DisplayName,
				journeys = journeyCount,
				reason,
			}
		);

	/// <inheritdoc/>
	public override void StepCompleted(
		TestStep step,
		int stepNumber,
		int totalSteps,
		string stepName,
		bool passed,
		string? detail
	) =>
		Post(
			new
			{
				type = "step-completed",
				config = step.Config.DisplayName,
				container = step.Container,
				step = step.StepName,
				journey = step.JourneyName,
				name = stepName,
				number = stepNumber,
				totalSteps,
				passed,
				detail,
			}
		);

	/// <inheritdoc/>
	protected override void ReportJourney(JourneyResult result) =>
		Post(
			new
			{
				type = "journey-completed",
				config = result.TestCase.Config.DisplayName,
				journey = result.TestCase.Journey.Name,
				passed = result.Passed,
				seconds = result.Duration.TotalSeconds,
				explanation = result.Explanation,
			}
		);

	/// <inheritdoc/>
	protected override void ReportRunFinished(int exitCode) => Post(new { type = "run-finished", exitCode });

	private void Post(object payload)
	{
		lock (postGate)
		{
			try
			{
				using var content = new StringContent(
					JsonSerializer.Serialize(payload, SerializerOptions),
					Encoding.UTF8,
					"application/json"
				);
				using var response = Client.PostAsync(url, content).GetAwaiter().GetResult();
			}
			catch (Exception ex)
				when (ex
						is HttpRequestException
							or TaskCanceledException
							or InvalidOperationException
							or UriFormatException
				)
			{
				// The listener is a spectator. A closed page, a stopped server, or an unreachable URL
				// costs the run its progress display, and must not cost it the journey being reported.
			}
		}
	}
}
