using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class JourneyRunnerTests
{
	[Test]
	public void ALoneFailureNamesTheStepsTheJourneyStoppedBefore() =>
		_ = JourneyRunner
			.Explain(["step 3/8: Tap Save — Element 'Save' not found after 60s."], 8, 3)
			.Should()
			.Be("step 3/8: Tap Save — Element 'Save' not found after 60s.\n  steps 4–8 were not run");

	[Test]
	public void OneStepNotRunIsNamedInTheSingular() =>
		_ = JourneyRunner.Explain(["step 7/8: Tap Save — boom"], 8, 7).Should().EndWith("\n  step 8 was not run");

	[Test]
	public void SeveralFailuresAreCountedAgainstTheStepsThatRan() =>
		_ = JourneyRunner
			.Explain(["step 2/5: A — differs", "step 4/5: B — differs"], 5, 5)
			.Should()
			.Be("2 of 5 steps failed:\n  • step 2/5: A — differs\n  • step 4/5: B — differs");
}
