using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class SuiteRunnerTests
{
	private static readonly IosPlatformConfig Fixture = new(
		"26.2",
		"iPhone 17 Pro",
		IsLightTheme: true,
		"com.example.app",
		"/path/to/app.app",
		100,
		210,
		3 * 2,
		0.005
	);

	private sealed record TestEnv : IJourneyEnvironment
	{
		public string Name => "Test";

		public string BackendUrl => "";

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpect() : Expectation
	{
		public override void Verify(TestDriver driver) { }
	}

	private static TestCase MakeCase(string name, int steps) =>
		new(
			Fixture,
			new JourneyDefinition(
				new TestEnv(),
				[new TestExpect()],
				[.. Enumerable.Range(1, steps).Select(_ => new JourneyStep(Dsl.None(), [new TestExpect()]))],
				[],
				name
			)
		);

	[Test]
	public void LongestFirstOrdersByStepCountDescending() =>
		_ = SuiteRunner
			.LongestFirst([MakeCase("short", 1), MakeCase("long", 5), MakeCase("mid", 3)])
			.Select(testCase => testCase.Journey.Name)
			.Should()
			.Equal("long", "mid", "short");
}
