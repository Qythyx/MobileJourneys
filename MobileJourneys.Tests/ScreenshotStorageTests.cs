using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Tests <see cref="ScreenshotStorage.FindExtraneous"/> against an
/// <see cref="InMemoryScreenshotStorage"/>.
/// </summary>
[TestFixture]
public sealed class ScreenshotStorageTests
{
	private InMemoryScreenshotStorage _storage = null!;
	private IosPlatformConfig _platform = null!;
	private FrameworkConfig _config = null!;
	private JourneyDefinition _journey = null!;

	[SetUp]
	public void SetUp()
	{
		_storage = new();
		_platform = new("26.2", "iPhone", IsLightTheme: true, "com.example.app", "/unused", 100, 210, 3 * 2, 0.005);
		_journey = new(
			new TestJourneyEnvironment(),
			[new TestExpectation("Initial")],
			[new JourneyStep(new Actions.None(), [new TestExpectation("FirstStep")])],
			[],
			"PrimaryJourney"
		);
		_config = new("Tests", [_platform], [_journey]);
	}

	private string InitialStep => $"01 {_journey.InitialName}";

	private TestStep K(string journey, string step) => new(_platform, journey, step, journey);

	private List<string> FindExtraneous(bool delete) => _storage.FindExtraneous(_config, delete);

	private sealed record TestJourneyEnvironment : IJourneyEnvironment
	{
		public string Name => "Test";

		public string BackendUrl => "";

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpectation(string? TargetLabel = null) : Expectation(TargetLabel)
	{
		public override void Verify(TestDriver driver) { }
	}

	[Test]
	public void ReturnsBaselineInUnknownContainer()
	{
		_storage.WriteBaseline(K("OrphanJourney", "01 Step"), [0]);

		var paths = FindExtraneous(delete: false);

		_ = paths.Should().ContainSingle().Which.Should().Be($"{_platform.DisplayName}/OrphanJourney/01 Step.png");
	}

	[Test]
	public void ReturnsOrphanedBaselineInValidContainer()
	{
		_storage.WriteBaseline(K(_journey.Name, InitialStep), [0]);
		_storage.WriteBaseline(K(_journey.Name, "99 RemovedStep"), [0]);

		var paths = FindExtraneous(delete: false);

		_ = paths
			.Should()
			.ContainSingle()
			.Which.Should()
			.Be($"{_platform.DisplayName}/{_journey.Name}/99 RemovedStep.png");
	}

	[Test]
	public void IgnoresFailureArtifactsOfExpectedSteps()
	{
		_storage.WriteBaseline(K(_journey.Name, InitialStep), [0]);
		_storage.WriteNewScreenshot(K(_journey.Name, InitialStep), [0]);
		_storage.WriteDiffImage(K(_journey.Name, InitialStep), 5.0, 1, [0]);
		_storage.WriteFailScreenshot(K(_journey.Name, InitialStep), "oops", [0]);
		_storage.WriteCrashLog(K(_journey.Name, InitialStep), "boom");

		var paths = FindExtraneous(delete: false);

		_ = paths.Should().BeEmpty();
	}

	[Test]
	public void ReturnsFailureArtifactsOfRemovedSteps()
	{
		_storage.WriteNewScreenshot(K(_journey.Name, "99 RemovedStep"), [0]);

		var paths = FindExtraneous(delete: false);

		_ = paths
			.Should()
			.ContainSingle()
			.Which.Should()
			.Be($"{_platform.DisplayName}/{_journey.Name}/99 RemovedStep [{_journey.Name}].new.png");
	}

	[Test]
	public void ReturnsFailureArtifactsAttributedToUnknownJourneys()
	{
		_storage.WriteNewScreenshot(new(_platform, _journey.Name, InitialStep, "GhostJourney"), [0]);

		var paths = FindExtraneous(delete: false);

		_ = paths.Should().ContainSingle().Which.Should().EndWith($"{InitialStep} [GhostJourney].new.png");
	}

	[Test]
	public void IgnoresExpectedBaselinesInNestedContainers()
	{
		var tree = new JourneyTree(
			new TestJourneyEnvironment(),
			[new TestExpectation("Initial")],
			[],
			[new Branch("TreeJourney", [new JourneyStep(new Actions.None(), [new TestExpectation("LeafStep")])], [])],
			null,
			"Root"
		);
		var config = new FrameworkConfig("Tests", [_platform], [.. tree.Flatten()]);
		foreach (var journey in config.Journeys)
		{
			foreach (var (container, stepName) in journey.ExpectedStepLocations())
			{
				_storage.WriteBaseline(new(_platform, container, stepName, journey.Name), [0]);
			}
		}

		var paths = _storage.FindExtraneous(config, deleteExtraneous: false);

		_ = paths.Should().BeEmpty();
	}

	[Test]
	public void DeleteTrueRemovesExtraneousBaselines()
	{
		_storage.WriteBaseline(K("OrphanJourney", "01 Step"), [0]);
		_storage.WriteBaseline(K(_journey.Name, "99 RemovedStep"), [0]);

		_ = FindExtraneous(delete: true);

		_ = _storage.BaselineExists(K("OrphanJourney", "01 Step")).Should().BeFalse();
		_ = _storage.BaselineExists(K(_journey.Name, "99 RemovedStep")).Should().BeFalse();
	}

	[Test]
	public void DeleteFalseLeavesFilesUntouched()
	{
		_storage.WriteBaseline(K("OrphanJourney", "01 Step"), [0]);
		_storage.WriteBaseline(K(_journey.Name, "99 RemovedStep"), [0]);

		_ = FindExtraneous(delete: false);

		_ = _storage.BaselineExists(K("OrphanJourney", "01 Step")).Should().BeTrue();
		_ = _storage.BaselineExists(K(_journey.Name, "99 RemovedStep")).Should().BeTrue();
	}

	[Test]
	public void ReturnsEmptyWhenPlatformContainerMissing() => _ = FindExtraneous(delete: false).Should().BeEmpty();
}
