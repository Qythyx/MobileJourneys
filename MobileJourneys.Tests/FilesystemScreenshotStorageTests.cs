using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Round-trips the <see cref="FilesystemScreenshotStorage"/> contract against a real
/// temp directory. Mirrors the surface that <see cref="InMemoryScreenshotStorage"/>
/// is asserted against, so the two implementations stay behaviorally aligned. Where
/// the on-disk filename layout matters (e.g., for `--list-extraneous` output), this
/// fixture also verifies the produced filenames directly.
/// </summary>
[TestFixture]
public sealed class FilesystemScreenshotStorageTests
{
	private string _tempRoot = null!;
	private FilesystemScreenshotStorage _storage = null!;
	private IosPlatformConfig _config = null!;

	[SetUp]
	public void SetUp()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), $"MobileJourneysTests-{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(_tempRoot);
		_storage = new(_tempRoot);
		_config = new("26.2", "iPhone", IsLightTheme: true, "com.example.app", "/unused", 2000);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	private TestStep K(string journey, string step) => new(_config, journey, step);

	private static JourneyDefinition J(string name) =>
		new(new TestJourneyEnvironment(), [new TestExpectation()], [], name);

	private sealed record TestJourneyEnvironment : IJourneyEnvironment
	{
		public string Name => "Test";

		public IReadOnlyDictionary<string, string> GetEnvVars() => new Dictionary<string, string>();

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpectation : Expectation
	{
		public override void Verify(TestDriver driver) { }
	}

	private string JourneyPath(string journeyName) => Path.Combine(_tempRoot, _config.DisplayName, journeyName);

	private string FilePath(string journeyName, string fileName) => Path.Combine(JourneyPath(journeyName), fileName);

	[Test]
	public void WriteBaselineCreatesContainersAndPersistsBytes()
	{
		var bytes = new byte[] { 1, 2, 3, 4 };

		_storage.WriteBaseline(K("Journey", "01 Step"), bytes);

		_ = _storage.BaselineExists(K("Journey", "01 Step")).Should().BeTrue();
		_ = _storage.ReadBaseline(K("Journey", "01 Step")).Should().BeEquivalentTo(bytes);
		_ = File.Exists(FilePath("Journey", "01 Step.png")).Should().BeTrue();
	}

	[Test]
	public void WriteNewScreenshotProducesDotNewPng()
	{
		_storage.WriteNewScreenshot(K("Journey", "01 Step"), [0]);

		_ = File.Exists(FilePath("Journey", "01 Step.new.png")).Should().BeTrue();
	}

	[Test]
	public void WriteDiffImageEncodesPercentageInFileName()
	{
		_storage.WriteDiffImage(K("Journey", "01 Step"), pixelErrorPercentage: 5.123, [0]);

		_ = File.Exists(FilePath("Journey", "01 Step_diff_5.123%.png")).Should().BeTrue();
	}

	[Test]
	public void WriteFailScreenshotEncodesSuffixInFileName()
	{
		_storage.WriteFailScreenshot(K("Journey", "01 Step"), suffix: "CRASH", [0]);

		_ = File.Exists(FilePath("Journey", "01 Step_FAIL_CRASH.png")).Should().BeTrue();
	}

	[Test]
	public void WriteCrashLogProducesUtf8TextFile()
	{
		_storage.WriteCrashLog(K("Journey", "01 Step"), "stack trace here");

		_ = File.ReadAllText(FilePath("Journey", "01 Step.CRASH.txt")).Should().Be("stack trace here");
	}

	[Test]
	public void HasFailureArtifactsTrueWhenAnyFailureKindPresent()
	{
		_storage.WriteNewScreenshot(K("Journey", "01 Step"), [0]);

		_ = _storage.HasFailureArtifacts(_config, J("Journey")).Should().BeTrue();
	}

	[Test]
	public void HasFailureArtifactsFalseWhenOnlyBaselinesPresent()
	{
		_storage.WriteBaseline(K("Journey", "01 Step"), [0]);

		_ = _storage.HasFailureArtifacts(_config, J("Journey")).Should().BeFalse();
	}

	[Test]
	public void DeleteFailureArtifactsForStepLeavesBaselineAndOtherStepsUntouched()
	{
		_storage.WriteBaseline(K("Journey", "01 Step"), [0]);
		_storage.WriteNewScreenshot(K("Journey", "01 Step"), [0]);
		_storage.WriteDiffImage(K("Journey", "01 Step"), 1.0, [0]);
		_storage.WriteFailScreenshot(K("Journey", "01 Step"), "CRASH", [0]);
		_storage.WriteCrashLog(K("Journey", "01 Step"), "boom");
		_storage.WriteNewScreenshot(K("Journey", "02 Other"), [0]);

		_storage.DeleteFailureArtifactsForStep(K("Journey", "01 Step"));

		_ = File.Exists(FilePath("Journey", "01 Step.png")).Should().BeTrue();
		_ = File.Exists(FilePath("Journey", "01 Step.new.png")).Should().BeFalse();
		_ = File.Exists(FilePath("Journey", "02 Other.new.png")).Should().BeTrue();
	}

	[Test]
	public void DeleteAllFailureArtifactsLeavesBaselinesIntact()
	{
		_storage.WriteBaseline(K("Journey", "01 Step"), [0]);
		_storage.WriteNewScreenshot(K("Journey", "01 Step"), [0]);
		_storage.WriteCrashLog(K("Journey", "01 Step"), "boom");

		_storage.DeleteAllFailureArtifacts(_config, J("Journey"));

		_ = File.Exists(FilePath("Journey", "01 Step.png")).Should().BeTrue();
		_ = File.Exists(FilePath("Journey", "01 Step.new.png")).Should().BeFalse();
		_ = File.Exists(FilePath("Journey", "01 Step.CRASH.txt")).Should().BeFalse();
	}

	[Test]
	public void DeleteFailureArtifactsForStepNoOpsWhenJourneyMissing()
	{
		Action act = () => _storage.DeleteFailureArtifactsForStep(K("Missing", "01 Step"));
		_ = act.Should().NotThrow();
	}

	[Test]
	public void DefaultRootsUnderTestAssemblyProjectRootPath()
	{
		var defaultStorage = FilesystemScreenshotStorage.Default();

		_ = defaultStorage.RootDir.Should().EndWith($"{Path.DirectorySeparatorChar}Screenshots");
	}
}
