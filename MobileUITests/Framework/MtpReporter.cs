using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.TestHost;

namespace MobileUITests.Framework;

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
	public void JourneyStarted(TestCase testCase) => Publish(testCase, InProgressTestNodeStateProperty.CachedInstance);

	public void StepStarted(TestCase testCase, int stepNumber, int totalSteps, string stepName) =>
		Publish(testCase, new InProgressTestNodeStateProperty($"Step {stepNumber}/{totalSteps}: {stepName}\n"));

	public void JourneyCompleted(JourneyResult result)
	{
		// Pass explanation = "" (empty, NOT null). The FailedTestNodeStateProperty ctor does
		// `base(explanation ?? exception.Message)` — null falls back to exception.Message,
		// which populates the IPC `reason` slot. `dotnet test`'s out-of-process reporter
		// (SDK-forked TerminalTestReporter) then renders `reason` AND `exceptions[0].ErrorMessage`,
		// duplicating the message. With explanation = "" the null-coalescing leaves Explanation
		// as "", the SDK skips the `informativeMessage` render (`!IsNullOrEmpty` check), and
		// ExceptionFlattener falls back to exception.Message for the remaining single render.
		// See: dotnet/sdk PR #49806 (introduced the bug), testfx ExceptionFlattener.
		TestNodeStateProperty state = result.Passed
			? PassedTestNodeStateProperty.CachedInstance
			: new FailedTestNodeStateProperty(result.Exception ?? new JourneyFailureException(result.Explanation), "");
		Publish(result.TestCase, state, result.Duration);
	}

	public void JourneySkipped(TestCase testCase, string explanation) =>
		Publish(testCase, new SkippedTestNodeStateProperty(explanation));

	public void TestsSkipped(string uid, string explanation)
	{
		var node = new TestNode { Uid = uid, DisplayName = uid };
		node.Properties.Add(new SkippedTestNodeStateProperty(explanation));
		messageBus.PublishAsync(producer, new TestNodeUpdateMessage(sessionUid, node)).GetAwaiter().GetResult();
	}

	private void Publish(TestCase testCase, IProperty property, TimeSpan? duration = null)
	{
		var node = TestNodeFactory.Create(testCase, testNodeNamespace);
		node.Properties.Add(property);
		if (duration is { } d)
		{
			var end = DateTimeOffset.UtcNow;
			var start = end - d;
			node.Properties.Add(new TimingProperty(new TimingInfo(start, end, d)));
		}
		messageBus.PublishAsync(producer, new TestNodeUpdateMessage(sessionUid, node)).GetAwaiter().GetResult();
	}
}
