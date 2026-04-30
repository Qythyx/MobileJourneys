using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.TestHost;

namespace MobileJourneys.Framework;

/// <summary>
/// Publishes journey lifecycle events as <see cref="TestNodeUpdateMessage"/> onto MTP's
/// <see cref="IMessageBus"/>. The dotnet test host's built-in terminal reporter renders these
/// live: InProgress on start, StandardOutput on each step, Passed/Failed on completion.
/// </summary>
/// <param name="messageBus">MTP message bus the framework was given in ExecuteRequestAsync.</param>
/// <param name="sessionUid">Session UID the framework was given in ExecuteRequestAsync.</param>
/// <param name="producer">Identity of the data producer (the test framework itself).</param>
/// <param name="testNodeNamespace">Namespace shown in IDE test explorers; passed through to TestNodeFactory.</param>
internal sealed class MtpReporter(
	IMessageBus messageBus,
	SessionUid sessionUid,
	IDataProducer producer,
	string testNodeNamespace
)
{
	public Task JourneyStartedAsync(TestCase testCase) =>
		PublishAsync(testCase, InProgressTestNodeStateProperty.CachedInstance);

	public Task StepStartedAsync(TestCase testCase, int stepNumber, int totalSteps, string stepName) =>
		PublishAsync(testCase, new InProgressTestNodeStateProperty($"Step {stepNumber}/{totalSteps}: {stepName}\n"));

	public Task JourneyCompletedAsync(JourneyResult result)
	{
		if (!result.Passed)
		{
			FailureSummary.RecordFailure(result.TestCase);
		}

		// When there's a real exception, pass explanation = "" (empty, NOT null). The
		// FailedTestNodeStateProperty(Exception, string) ctor does
		// `base(explanation ?? exception.Message)` — null falls back to exception.Message,
		// which populates the IPC `reason` slot. `dotnet test`'s out-of-process reporter
		// (SDK-forked TerminalTestReporter) then renders `reason` AND `exceptions[0].ErrorMessage`,
		// duplicating the message. With explanation = "" the null-coalescing leaves Explanation
		// as "", the SDK skips the `informativeMessage` render (`!IsNullOrEmpty` check), and
		// ExceptionFlattener falls back to exception.Message for the remaining single render.
		// See: dotnet/sdk PR #49806 (introduced the bug), testfx ExceptionFlattener.
		//
		// When there's no exception (e.g., a synthesized failure), use the explanation-only
		// ctor so the reporter doesn't show a fake "InvalidOperationException" type.
		TestNodeStateProperty state =
			result.Passed ? PassedTestNodeStateProperty.CachedInstance
			: result.Exception is { } exception ? new FailedTestNodeStateProperty(exception, "")
			: new FailedTestNodeStateProperty(result.Explanation);
		return PublishAsync(result.TestCase, state, result.Duration);
	}

	public Task JourneySkippedAsync(TestCase testCase, string explanation) =>
		PublishAsync(testCase, new SkippedTestNodeStateProperty(explanation));

	public Task TestsSkippedAsync(string uid, string explanation)
	{
		var node = new TestNode { Uid = uid, DisplayName = uid };
		node.Properties.Add(new SkippedTestNodeStateProperty(explanation));
		return messageBus.PublishAsync(producer, new TestNodeUpdateMessage(sessionUid, node));
	}

	private Task PublishAsync(TestCase testCase, IProperty property, TimeSpan? duration = null)
	{
		var node = TestNodeFactory.Create(testCase, testNodeNamespace);
		node.Properties.Add(property);
		if (duration is { } d)
		{
			var end = DateTimeOffset.UtcNow;
			var start = end - d;
			node.Properties.Add(new TimingProperty(new TimingInfo(start, end, d)));
		}
		return messageBus.PublishAsync(producer, new TestNodeUpdateMessage(sessionUid, node));
	}
}
