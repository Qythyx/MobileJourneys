using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Tests <see cref="JourneyTree.Flatten"/>: journey construction from childless nodes, step
/// concatenation along the root→leaf path, per-step container assignment, depth-based numbering,
/// and construction-time validation of the tree shape.
/// </summary>
[TestFixture]
public sealed class JourneyTreeTests
{
	private static readonly TestJourneyEnvironment Env = new();

	private static JourneyStep Step(string target) => new(new TestAction(target), [new TestExpectation(target)]);

	private sealed record TestJourneyEnvironment : IJourneyEnvironment
	{
		public string Name => "Test";

		public string BackendUrl => "";

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestAction(string? Target = null) : JourneyAction(Target)
	{
		public override void Execute(TestDriver driver) { }
	}

	private sealed record TestExpectation(string? Target = null) : Expectation(Target)
	{
		public override void Verify(TestDriver driver) { }
	}

	[Test]
	public void ChildlessTreeFlattensToSingleJourneyNamedAfterRoot()
	{
		var tree = new JourneyTree(Env, [new TestExpectation("Initial")], [Step("Only")], [], null, "Solo");

		var journeys = tree.Flatten().ToList();

		var journey = journeys.Should().ContainSingle().Which;
		_ = journey.Name.Should().Be("Solo");
		_ = journey.Steps.Should().ContainSingle();
		_ = journey
			.ExpectedStepLocations()
			.Should()
			.Equal(("Solo", "01 TestExpectation Initial"), ("Solo", "02 TestAction Only"));
	}

	[Test]
	public void ChildlessNodesFlattenToJourneysWithConcatenatedSteps()
	{
		var tree = new JourneyTree(
			Env,
			[new TestExpectation("Initial")],
			[Step("RootStep")],
			[
				new Branch(
					"Menu",
					[Step("OpenMenu")],
					[new Branch("About", [Step("TapAbout")], []), new Branch("Settings", [Step("TapSettings")], [])]
				),
				new Branch("PullToRefresh", [Step("Refresh")], []),
			],
			null,
			"Home"
		);

		var journeys = tree.Flatten().ToList();

		_ = journeys.Select(j => j.Name).Should().Equal("About", "Settings", "PullToRefresh");
		var about = journeys[0];
		_ = about.Scenario.Should().BeSameAs(Env);
		_ = about.InitialExpect.Should().Equal(tree.InitialExpect);
		_ = about
			.ExpectedStepLocations()
			.Should()
			.Equal(
				("Home", "01 TestExpectation Initial"),
				("Home", "02 TestAction RootStep"),
				("Home/Menu", "03 TestAction OpenMenu"),
				("Home/Menu/About", "04 TestAction TapAbout")
			);
	}

	[Test]
	public void SharedStepsGetTheSameLocationInEveryJourney()
	{
		var tree = new JourneyTree(
			Env,
			[new TestExpectation("Initial")],
			[],
			[
				new Branch(
					"Menu",
					[Step("OpenMenu")],
					[new Branch("About", [Step("TapAbout")], []), new Branch("ContactUs", [Step("TapContact")], [])]
				),
			],
			null,
			"Home"
		);

		var locations = tree.Flatten().Select(j => j.ExpectedStepLocations().ToList()).ToList();

		_ = locations[0][1].Should().Be(locations[1][1]);
		_ = locations[0][2].Should().NotBe(locations[1][2]);
	}

	[Test]
	public void InitialMaskElementsPropagateToEveryJourney()
	{
		string[] masks = ["Spinner"];
		var tree = new JourneyTree(
			Env,
			[new TestExpectation("Initial")],
			[],
			[new Branch("A", [Step("A")], []), new Branch("B", [Step("B")], [])],
			masks,
			"Root"
		);

		_ = tree.Flatten().Select(j => j.InitialMaskElements).Should().AllSatisfy(m => m.Should().BeSameAs(masks));
	}

	[Test]
	public void NodeWithNeitherStepsNorChildrenThrows()
	{
		var ctor = () => _ = new Branch("Flyout", [], []);
		_ = ctor.Should().Throw<ArgumentException>().WithMessage("*Flyout*neither steps nor children*");
	}

	[Test]
	public void TreeRootMayHaveNoStepsBecauseItsInitialScreenshotIsTheTest()
	{
		var tree = new JourneyTree(Env, [new TestExpectation("Initial")], [], [], null, "EmptyOffers");

		_ = tree.Flatten().Single().ExpectedStepLocations().Should().ContainSingle();
	}

	[Test]
	public void SiblingsWithDuplicateNamesThrow()
	{
		var ctor = () =>
			_ = new Branch(
				"Menu",
				[Step("Menu")],
				[new Branch("Dup", [Step("X")], []), new Branch("Dup", [Step("Y")], [])]
			);
		_ = ctor.Should().Throw<ArgumentException>().WithMessage("*Menu*duplicate*Dup*");
	}

	[Test]
	public void TreeChildrenWithDuplicateNamesThrow()
	{
		var ctor = () =>
			_ = new JourneyTree(
				Env,
				[new TestExpectation("Initial")],
				[],
				[new Branch("Dup", [Step("X")], []), new Branch("Dup", [Step("Y")], [])],
				null,
				"Root"
			);
		_ = ctor.Should().Throw<ArgumentException>().WithMessage("*Root*duplicate*Dup*");
	}
}
