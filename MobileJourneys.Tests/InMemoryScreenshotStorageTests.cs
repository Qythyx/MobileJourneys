using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Locks down the in-memory storage's typed-method semantics so they don't drift from
/// <see cref="FilesystemScreenshotStorage"/> behavior. The two implementations are tested
/// in parallel against the same scenarios.
/// </summary>
[TestFixture]
public sealed class InMemoryScreenshotStorageTests
{
	private InMemoryScreenshotStorage _storage = null!;
	private IosPlatformConfig _config = null!;

	[SetUp]
	public void SetUp()
	{
		_storage = new();
		_config = new("26.2", "iPhone", IsLightTheme: true, "com.example.app", "/unused");
	}

	private TestStep K(string journey, string step) => new(_config, journey, step, journey);

	private static JourneyDefinition J(string name) =>
		new(new TestJourneyEnvironment(), [new TestExpectation()], [], [], name);

	private sealed record TestJourneyEnvironment : IJourneyEnvironment
	{
		public string Name => "Test";

		public string BackendUrl => "";

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpectation : Expectation
	{
		public override void Verify(TestDriver driver) { }
	}

	[Test]
	public void WriteBaselineThenReadBaselineRoundTripsBytes()
	{
		var bytes = new byte[] { 1, 2, 3, 4 };

		_storage.WriteBaseline(K("Journey", "01 Step"), bytes);

		_ = _storage.ReadBaseline(K("Journey", "01 Step")).Should().BeEquivalentTo(bytes);
		_ = _storage.BaselineExists(K("Journey", "01 Step")).Should().BeTrue();
	}

	[Test]
	public void ReadBaselineThrowsWhenMissing()
	{
		Action act = () => _storage.ReadBaseline(K("Journey", "missing"));
		_ = act.Should().Throw<FileNotFoundException>();
	}

	[Test]
	public void WriteNewScreenshotIsObservableViaTestHelper()
	{
		_storage.WriteNewScreenshot(K("Journey", "01 Step"), [0]);

		_ = _storage.NewScreenshotExists(K("Journey", "01 Step")).Should().BeTrue();
		_ = _storage.BaselineExists(K("Journey", "01 Step")).Should().BeFalse();
	}

	[Test]
	public void WriteDiffImageEncodesPercentageInFileName()
	{
		_storage.WriteDiffImage(K("Journey", "01 Step"), pixelErrorPercentage: 5.123, pixelErrorCount: 42, [0]);

		_ = _storage.DiffImageExists(K("Journey", "01 Step")).Should().BeTrue();
		_ = _storage.ListAllFiles(_config, "Journey").Should().Contain("01 Step [Journey]_diff_5.123%_42px.png");
	}

	[Test]
	public void WriteFailScreenshotEncodesSuffixInFileName()
	{
		_storage.WriteFailScreenshot(K("Journey", "01 Step"), suffix: "CRASH", [0]);

		_ = _storage.FailScreenshotExists(K("Journey", "01 Step")).Should().BeTrue();
		_ = _storage.ListAllFiles(_config, "Journey").Should().Contain("01 Step [Journey]_FAIL_CRASH.png");
	}

	[Test]
	public void WriteCrashLogStoresUtf8Bytes()
	{
		_storage.WriteCrashLog(K("Journey", "01 Step"), "hello world");

		_ = _storage.CrashLogExists(K("Journey", "01 Step")).Should().BeTrue();
		_ = _storage
			.ReadRaw(_config, "Journey", "01 Step [Journey].CRASH.txt")
			.Should()
			.BeEquivalentTo("hello world"u8.ToArray());
	}

	[Test]
	public void HasFailureArtifactsTrueForEachKind()
	{
		_storage.WriteNewScreenshot(K("JNew", "01 Step"), [0]);
		_storage.WriteDiffImage(K("JDiff", "01 Step"), 1.0, 1, [0]);
		_storage.WriteFailScreenshot(K("JFail", "01 Step"), "CRASH", [0]);
		_storage.WriteCrashLog(K("JCrash", "01 Step"), "boom");

		_ = _storage.HasFailureArtifacts(_config, J("JNew")).Should().BeTrue();
		_ = _storage.HasFailureArtifacts(_config, J("JDiff")).Should().BeTrue();
		_ = _storage.HasFailureArtifacts(_config, J("JFail")).Should().BeTrue();
		_ = _storage.HasFailureArtifacts(_config, J("JCrash")).Should().BeTrue();
	}

	[Test]
	public void HasFailureArtifactsFalseWhenOnlyBaselinesPresent()
	{
		_storage.WriteBaseline(K("Journey", "01 Step"), [0]);

		_ = _storage.HasFailureArtifacts(_config, J("Journey")).Should().BeFalse();
	}

	[Test]
	public void HasFailureArtifactsFalseWhenJourneyMissing() =>
		_ = _storage.HasFailureArtifacts(_config, J("Missing")).Should().BeFalse();

	[Test]
	public void DeleteFailureArtifactsForStepRemovesAllFourKindsForOneStep()
	{
		_storage.WriteBaseline(K("Journey", "01 Step"), [0]);
		_storage.WriteNewScreenshot(K("Journey", "01 Step"), [0]);
		_storage.WriteDiffImage(K("Journey", "01 Step"), 5.0, 1, [0]);
		_storage.WriteFailScreenshot(K("Journey", "01 Step"), "CRASH", [0]);
		_storage.WriteCrashLog(K("Journey", "01 Step"), "boom");
		// A different step's artifacts must not be touched.
		_storage.WriteNewScreenshot(K("Journey", "02 Other"), [0]);

		_storage.DeleteFailureArtifactsForStep(K("Journey", "01 Step"));

		_ = _storage.BaselineExists(K("Journey", "01 Step")).Should().BeTrue();
		_ = _storage.NewScreenshotExists(K("Journey", "01 Step")).Should().BeFalse();
		_ = _storage.DiffImageExists(K("Journey", "01 Step")).Should().BeFalse();
		_ = _storage.FailScreenshotExists(K("Journey", "01 Step")).Should().BeFalse();
		_ = _storage.CrashLogExists(K("Journey", "01 Step")).Should().BeFalse();
		_ = _storage.NewScreenshotExists(K("Journey", "02 Other")).Should().BeTrue();
	}

	[Test]
	public void DeleteFailureArtifactsForStepNoOpsWhenJourneyMissing()
	{
		Action act = () => _storage.DeleteFailureArtifactsForStep(K("Missing", "01 Step"));
		_ = act.Should().NotThrow();
	}

	[Test]
	public void DeleteAllFailureArtifactsRemovesEveryKindAndLeavesBaselines()
	{
		_storage.WriteBaseline(K("Journey", "01 Step"), [0]);
		_storage.WriteBaseline(K("Journey", "02 Step"), [0]);
		_storage.WriteNewScreenshot(K("Journey", "01 Step"), [0]);
		_storage.WriteDiffImage(K("Journey", "01 Step"), 5.0, 1, [0]);
		_storage.WriteFailScreenshot(K("Journey", "02 Step"), "CRASH", [0]);
		_storage.WriteCrashLog(K("Journey", "02 Step"), "boom");

		_storage.DeleteAllFailureArtifacts(_config, J("Journey"));

		_ = _storage.ListAllFiles(_config, "Journey").Should().BeEquivalentTo(["01 Step.png", "02 Step.png"]);
	}

	[Test]
	public void HasFailureArtifactsDistinguishesJourneysSharingAContainer()
	{
		var aboutJourney = J("About") with { StepContainers = ["Home/Menu"] };
		var contactJourney = J("ContactUs") with { StepContainers = ["Home/Menu"] };
		_storage.WriteNewScreenshot(new(_config, "Home/Menu", "02 Step", "About"), [0]);

		_ = _storage.HasFailureArtifacts(_config, aboutJourney).Should().BeTrue();
		_ = _storage.HasFailureArtifacts(_config, contactJourney).Should().BeFalse();
	}
}
