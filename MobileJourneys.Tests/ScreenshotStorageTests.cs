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
		_platform = new("26.2", "iPhone", IsLightTheme: true, "com.example.app", "/unused");
		_journey = new(
			new TestJourneyEnvironment(),
			[new TestExpectation("Initial")],
			[new JourneyStep(new Actions.None(), [new TestExpectation("FirstStep")])],
			"PrimaryJourney"
		);
		_config = new("Tests", "Tests", "MobileJourneys.Tests", "test", [_platform], [_journey]);
	}

	private string InitialStep => $"01 {_journey.InitialName}";

	private TestStep K(string journey, string step) => new(_platform, journey, step);

	private List<string> FindExtraneous(bool delete) =>
		_storage.FindExtraneous(_config, j => j.ExpectedStepNames(), delete);

	private sealed record TestJourneyEnvironment : IJourneyEnvironment
	{
		public string Name => "Test";

		public IReadOnlyDictionary<string, string> GetEnvVars() => new Dictionary<string, string>();

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpectation(string? TargetLabel = null) : Expectation(TargetLabel)
	{
		public override void Verify(TestDriver driver) { }
	}

	[Test]
	public void ReturnsOrphanedJourneyFolder()
	{
		_storage.WriteBaseline(K("OrphanJourney", "01 Step"), [0]);

		var paths = FindExtraneous(delete: false);

		_ = paths.Should().Contain($"{_platform.DisplayName}/OrphanJourney/");
	}

	[Test]
	public void ReturnsOrphanedBaselineInValidJourney()
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
	public void IgnoresFailureArtifactsInValidJourneys()
	{
		_storage.WriteBaseline(K(_journey.Name, InitialStep), [0]);
		_storage.WriteNewScreenshot(K(_journey.Name, "01 Step"), [0]);
		_storage.WriteDiffImage(K(_journey.Name, "01 Step"), 5.0, [0]);
		_storage.WriteFailScreenshot(K(_journey.Name, "01 Step"), "oops", [0]);

		var paths = FindExtraneous(delete: false);

		_ = paths.Should().BeEmpty();
	}

	[Test]
	public void DeleteTrueRemovesExtraneousFolders()
	{
		_storage.WriteBaseline(K("OrphanJourney", "01 Step"), [0]);

		_ = FindExtraneous(delete: true);

		_ = _storage.BaselineExists(K("OrphanJourney", "01 Step")).Should().BeFalse();
	}

	[Test]
	public void DeleteTrueRemovesExtraneousBaselines()
	{
		_storage.WriteBaseline(K(_journey.Name, "99 RemovedStep"), [0]);

		_ = FindExtraneous(delete: true);

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
	public void FormatsFolderPathsWithTrailingSeparator()
	{
		_storage.WriteBaseline(K("OrphanJourney", "01 Step"), [0]);

		var paths = FindExtraneous(delete: false);

		_ = paths.Should().ContainSingle().Which.Should().EndWith("/");
	}

	[Test]
	public void ReturnsEmptyWhenPlatformContainerMissing() => _ = FindExtraneous(delete: false).Should().BeEmpty();
}
