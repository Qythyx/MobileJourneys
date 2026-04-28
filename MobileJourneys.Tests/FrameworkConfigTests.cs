using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class FrameworkConfigTests
{
	[Test]
	public void FrameworkConfig_PreservesAllConstructorArguments()
	{
		var platform = new IosPlatformConfig("26.2", "iPhone", true, "com.example.app", "/path/app");
		var journey = new JourneyDefinition(new TestEnv(), [new TestExpectation()], [], "TestJourney");

		var config = new FrameworkConfig(
			DisplayName: "Display",
			Description: "Desc",
			TestNodeNamespace: "Foo.Bar",
			DeepLinkScheme: "scheme",
			PlatformConfigs: [platform],
			Journeys: [journey]
		);

		config.DisplayName.Should().Be("Display");
		config.Description.Should().Be("Desc");
		config.TestNodeNamespace.Should().Be("Foo.Bar");
		config.DeepLinkScheme.Should().Be("scheme");
		config.PlatformConfigs.Should().ContainSingle().Which.Should().BeSameAs(platform);
		config.Journeys.Should().ContainSingle().Which.Should().BeSameAs(journey);
	}

	[Test]
	public void JourneyDefinition_ThrowsWhenInitialExpectIsEmpty()
	{
		Action ctor = () => new JourneyDefinition(new TestEnv(), [], [], "j");
		ctor.Should().Throw<ArgumentException>().WithParameterName("InitialExpect");
	}

	[Test]
	public void JourneyDefinition_InitialName_DerivesFromFirstExpectationLabel()
	{
		var journey = new JourneyDefinition(new TestEnv(), [new TestExpectation("MyTarget")], [], "j");
		journey.InitialName.Should().Be("TestExpectation MyTarget");
	}

	private sealed record TestEnv : IJourneyEnvironment
	{
		public string Name => "Test";

		public IReadOnlyDictionary<string, string> GetEnvVars() => new Dictionary<string, string>();

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpectation(string? Target = null) : Expectation(Target)
	{
		public override void Verify(TestDriver driver) { }
	}
}
