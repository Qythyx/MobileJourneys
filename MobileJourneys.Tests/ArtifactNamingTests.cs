using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class ArtifactNamingTests
{
	private static readonly AndroidPlatformConfig Config = new(
		"15",
		"Pixel 8",
		"Pixel_API35",
		IsLightTheme: true,
		"com.example.app",
		"/path/Signed.apk",
		null,
		200,
		550,
		3 * 10,
		0.005
	);

	private static TestStep Step => new(Config, "Journey", "01 Step", "Journey");

	[Test]
	public void DiffFileNameRoundTripsPercentageAndPixelCount()
	{
		var fileName = ArtifactNaming.DiffFileName(Step, pixelErrorPercentage: 5.123, pixelErrorCount: 42);

		var parsed = ArtifactNaming.ParseFailureArtifact(fileName);

		_ = parsed.Should().NotBeNull();
		_ = parsed!.Kind.Should().Be("diff");
		_ = parsed.DiffPercent.Should().Be(5.123);
		_ = parsed.DiffPixelCount.Should().Be(42);
	}

	[Test]
	public void DiffFileNameKeepsTheCountWhenThePercentageRoundsToZero()
	{
		var fileName = ArtifactNaming.DiffFileName(Step, pixelErrorPercentage: 0.00004, pixelErrorCount: 1);

		var parsed = ArtifactNaming.ParseFailureArtifact(fileName);

		_ = parsed!.DiffPercent.Should().Be(0);
		_ = parsed.DiffPixelCount.Should().Be(1);
	}

	[Test]
	public void DiffFileNameWrittenBeforeCountsWereRecordedStillParses()
	{
		var parsed = ArtifactNaming.ParseFailureArtifact("01 Step [Journey]_diff_5.123%.png");

		_ = parsed.Should().NotBeNull();
		_ = parsed!.Kind.Should().Be("diff");
		_ = parsed.DiffPercent.Should().Be(5.123);
		_ = parsed.DiffPixelCount.Should().BeNull();
	}

	[Test]
	public void ErrorTextIsAFailureArtifactOfItsOwnStepAndJourney()
	{
		var fileName = ArtifactNaming.ErrorTextFileName(Step);

		// Unrecognised, it would read as an orphan to the extraneous scan and survive a rerun's cleanup.
		_ = ArtifactNaming.IsFailureArtifact(fileName).Should().BeTrue();
		_ = ArtifactNaming.IsFailureArtifactForStep(fileName, "01 Step", "Journey").Should().BeTrue();
		_ = ArtifactNaming.IsFailureArtifactForJourney(fileName, "Journey").Should().BeTrue();
		_ = ArtifactNaming.IsBaseline(fileName).Should().BeFalse();
		_ = ArtifactNaming.ParseFailureArtifact(fileName)!.Kind.Should().Be("error");
	}

	[Test]
	public void NonDiffArtifactsCarryNoPixelCount()
	{
		_ = ArtifactNaming.ParseFailureArtifact("01 Step [Journey].new.png")!.DiffPixelCount.Should().BeNull();
		_ = ArtifactNaming.ParseFailureArtifact("01 Step [Journey]_FAIL_boom.png")!.DiffPixelCount.Should().BeNull();
	}
}
