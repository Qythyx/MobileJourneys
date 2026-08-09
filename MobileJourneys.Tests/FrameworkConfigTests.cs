using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class FrameworkConfigTests
{
	[Test]
	public void FrameworkConfigPreservesAllConstructorArguments()
	{
		var platform = new IosPlatformConfig("26.2", "iPhone", true, "com.example.app", "/path/app", 100, 210, 3 * 2, 0.005);
		var journey = new JourneyDefinition(new TestEnv(), [new TestExpectation()], [], [], "TestJourney");
		var config = new FrameworkConfig("Display", [platform], [journey]);

		_ = config.DisplayName.Should().Be("Display");
		_ = config.PlatformConfigs.Should().ContainSingle().Which.Should().BeSameAs(platform);
		_ = config.Journeys.Should().ContainSingle().Which.Should().BeSameAs(journey);
	}

	[Test]
	public void FrameworkConfigThrowsOnDuplicateJourneyNames()
	{
		var platform = new IosPlatformConfig("26.2", "iPhone", true, "com.example.app", "/path/app", 100, 210, 3 * 2, 0.005);
		var journeyA = new JourneyDefinition(new TestEnv(), [new TestExpectation()], [], [], "Dup");
		var journeyB = new JourneyDefinition(new TestEnv(), [new TestExpectation()], [], [], "Dup");

		var ctor = () => _ = new FrameworkConfig("D", [platform], [journeyA, journeyB]);

		_ = ctor.Should().Throw<ArgumentException>().WithMessage("*unique*Dup*");
	}

	[Test]
	public void JourneyDefinitionThrowsWhenInitialExpectIsEmpty()
	{
		var ctor = () => _ = new JourneyDefinition(new TestEnv(), [], [], [], "j");
		_ = ctor.Should().Throw<ArgumentException>().WithParameterName("InitialExpect");
	}

	[Test]
	public void JourneyDefinitionInitialNameDerivesFromFirstExpectationLabel()
	{
		var journey = new JourneyDefinition(new TestEnv(), [new TestExpectation("MyTarget")], [], [], "j");
		_ = journey.InitialName.Should().Be("TestExpectation MyTarget");
	}

	[Test]
	public void JourneyDefinitionDefaultsInitialMaskElementsToNullWhenOmitted()
	{
		var journey = new JourneyDefinition(new TestEnv(), [new TestExpectation()], []);
		_ = journey.InitialMaskElements.Should().BeNull();
	}

	[Test]
	public void JourneyDefinitionPreservesInitialMaskElements()
	{
		string[] masks = ["Spinner", "Clock"];
		var journey = new JourneyDefinition(new TestEnv(), [new TestExpectation()], [], masks, "j");
		_ = journey.InitialMaskElements.Should().BeSameAs(masks);
	}

	private sealed record TestEnv : IJourneyEnvironment
	{
		public string Name => "Test";

		public string BackendUrl => "";

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpectation(string? Target = null) : Expectation(Target)
	{
		public override void Verify(TestDriver driver) { }
	}
}
