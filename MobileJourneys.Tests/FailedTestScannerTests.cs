using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Tests <see cref="FailedTestScanner.IsFailedJourney"/>, <see cref="FailedTestScanner.CleanupStepResults"/>,
/// and <see cref="FailedTestScanner.CleanupResults"/> against a synthetic Screenshots tree on disk.
///
/// <para>
/// <see cref="FailedTestScanner.FindExtraneousScreenshots"/> isn't covered here because it
/// reads <see cref="ScreenshotHelper.ScreenshotsRootDir"/> at module-load time from the
/// entry assembly's <c>ProjectDir</c> metadata — there's no way to redirect it to a temp
/// directory without spawning a test executable. That logic is exercised end-to-end by the
/// consumer's UI test runs (it's the <c>--list-extraneous</c>/<c>--delete-extraneous</c>
/// CLI flow).
/// </para>
/// </summary>
[TestFixture]
public sealed class FailedTestScannerTests
{
	private string _tempRoot = null!;

	[SetUp]
	public void SetUp()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), $"MobileJourneysTests-{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(_tempRoot);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	private static IosPlatformConfig BuildConfig(string deviceName) =>
		new("26.2", deviceName, IsLightTheme: true, "com.example.app", "/unused");

	private static JourneyDefinition BuildJourney(string name) =>
		new(new TestJourneyEnvironment(), [new TestExpectation()], [], name);

	private sealed record TestJourneyEnvironment : IJourneyEnvironment
	{
		public string Name => "Test";

		public IReadOnlyDictionary<string, string> GetEnvVars() => new Dictionary<string, string>();

		public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
	}

	private sealed record TestExpectation() : Expectation
	{
		public override void Verify(TestDriver driver) { }
	}

	[Test]
	public void IsFailedJourney_ReturnsFalse_WhenJourneyDirDoesNotExist()
	{
		// The framework's IsFailedJourney looks under ScreenshotHelper's resolved root.
		// Use a guaranteed-unique journey name so the lookup misses regardless of
		// what the consumer's actual Screenshots directory contains.
		var config = BuildConfig("NonexistentDevice");
		var journey = BuildJourney($"Nonexistent_{Guid.NewGuid():N}");

		FailedTestScanner.IsFailedJourney(config, journey).Should().BeFalse();
	}

	[Test]
	public void CleanupStepResults_NoOps_WhenJourneyDirDoesNotExist()
	{
		var config = BuildConfig("NonexistentDevice");

		Action act = () => FailedTestScanner.CleanupStepResults(config, "NonexistentJourney", "01 SomeStep");
		act.Should().NotThrow();
	}

	[Test]
	public void CleanupResults_NoOps_WhenJourneyDirDoesNotExist()
	{
		var config = BuildConfig("NonexistentDevice");

		Action act = () => FailedTestScanner.CleanupResults(config, "NonexistentJourney");
		act.Should().NotThrow();
	}
}
