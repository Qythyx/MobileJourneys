using AwesomeAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class ScreenshotManagerTests
{
	[Test]
	public void ScaleToMaxDownscalesProportionallyWhenImageExceedsMaxHeight()
	{
		using var image = new Image<Rgb24>(2000, 4000);

		image.ScaleToMax(maxHeight: 1000);

		_ = image.Height.Should().Be(1000);
		_ = image.Width.Should().Be(500);
	}

	[Test]
	public void ScaleToMaxLeavesImageUntouchedWhenAlreadyUnderMaxHeight()
	{
		using var image = new Image<Rgb24>(800, 600);

		image.ScaleToMax(maxHeight: 1000);

		_ = image.Width.Should().Be(800);
		_ = image.Height.Should().Be(600);
	}

	[Test]
	public void ScaleToMaxLeavesImageUntouchedWhenExactlyAtMaxHeight()
	{
		using var image = new Image<Rgb24>(500, 1000);

		image.ScaleToMax(maxHeight: 1000);

		_ = image.Width.Should().Be(500);
		_ = image.Height.Should().Be(1000);
	}

	[Test]
	public void ScaleMaskRegionsScalesAllFourBoundsProportionally()
	{
		var regions = new[] { new Rectangle(100, 200, 300, 400) };

		var scaled = ImageHelpers.ScaleMaskRegions(regions, from: new Size(1000, 2000), to: new Size(500, 1000));

		_ = scaled.Should().ContainSingle();
		var r = scaled[0];
		_ = r.X.Should().Be(50);
		_ = r.Y.Should().Be(100);
		_ = r.Width.Should().Be(150);
		_ = r.Height.Should().Be(200);
	}

	[Test]
	public void ScaleMaskRegionsReturnsInputUnchangedWhenNoRegions()
	{
		var regions = Array.Empty<Rectangle>();

		var scaled = ImageHelpers.ScaleMaskRegions(regions, from: new Size(100, 100), to: new Size(50, 50));

		_ = scaled.Should().BeSameAs(regions);
	}

	[Test]
	public void ScaleMaskRegionsReturnsInputUnchangedWhenSourceWidthIsZero()
	{
		// Defensive case: from-size of (0, …) would div-by-zero; method bails out.
		var regions = new[] { new Rectangle(10, 10, 20, 20) };

		var scaled = ImageHelpers.ScaleMaskRegions(regions, from: new Size(0, 100), to: new Size(50, 50));

		_ = scaled.Should().BeSameAs(regions);
	}

	[Test]
	public void AreImagesStableReturnsTrueForIdenticalImages()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));

		_ = ImageHelpers.AreImagesEqual(a, b, []).Should().BeTrue();
	}

	[Test]
	public void AreImagesStableReturnsFalseForDifferentImagesWithoutMask()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(100, 100, new Rgb24(0, 255, 0));

		_ = ImageHelpers.AreImagesEqual(a, b, []).Should().BeFalse();
	}

	[Test]
	public void AreImagesStableReturnsTrueWhenDifferenceIsEntirelyInsideMask()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));

		// Differ in a single 10×10 region; mask covers exactly that region.
		b.ProcessPixelRows(accessor =>
		{
			for (var y = 10; y < 20; y++)
			{
				var row = accessor.GetRowSpan(y);
				for (var x = 10; x < 20; x++)
				{
					row[x] = new Rgb24(0, 255, 0);
				}
			}
		});

		var mask = new[] { new Rectangle(10, 10, 10, 10) };

		_ = ImageHelpers.AreImagesEqual(a, b, mask).Should().BeTrue();
	}

	[Test]
	public void AreImagesStableReturnsFalseWhenDifferenceIsOutsideMask()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));

		// Differ at (50,50); mask covers (10,10,10×10) elsewhere.
		b.ProcessPixelRows(accessor => accessor.GetRowSpan(50)[50] = new Rgb24(0, 255, 0));

		var mask = new[] { new Rectangle(10, 10, 10, 10) };

		_ = ImageHelpers.AreImagesEqual(a, b, mask).Should().BeFalse();
	}

	[Test]
	public void AreImagesStableThrowsArgumentExceptionWhenDimensionsDiffer()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(101, 100, new Rgb24(255, 0, 0));

		Action act = () => ImageHelpers.AreImagesEqual(a, b, []);
		_ = act.Should().Throw<ArgumentException>().WithMessage("*same size*");
	}

	// --- CompareWithBaselineAndDispose (instance API) ---

	private static IosPlatformConfig BuildConfig() =>
		new("26.2", "iPhone", IsLightTheme: true, "com.example.app", "/unused", MaxScreenshotHeight: 100);

	[Test]
	public void CompareWithBaselineAndDisposeWritesBaselineWhenAbsent()
	{
		var storage = new InMemoryScreenshotStorage();
		var manager = new ScreenshotManager(storage);
		var config = BuildConfig();
		var key = new TestStep(config, "Journey", "01 Step");
		var actual = new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0));

		var result = manager.CompareWithBaselineAndDispose(actual, key, []);

		_ = result.Passed.Should().BeTrue();
		_ = result.PixelDiffPercentage.Should().Be(0);
		_ = result.ReportPath.Should().BeNull();
		_ = storage.BaselineExists(key).Should().BeTrue();
	}

	[Test]
	public void CompareWithBaselineAndDisposeReturnsPassedWithoutWritingArtifactsWhenMatch()
	{
		var storage = new InMemoryScreenshotStorage();
		var manager = new ScreenshotManager(storage);
		var config = BuildConfig();
		var key = new TestStep(config, "Journey", "01 Step");

		// Seed an existing baseline by running once with no baseline present.
		_ = manager.CompareWithBaselineAndDispose(new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0)), key, []);

		var result = manager.CompareWithBaselineAndDispose(new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0)), key, []);

		_ = result.Passed.Should().BeTrue();
		_ = result.ReportPath.Should().BeNull();
		_ = storage.ListAllFiles(config, "Journey").Should().ContainSingle().Which.Should().Be("01 Step.png");
	}

	[Test]
	public void CompareWithBaselineAndDisposeWritesNewAndDiffWhenMismatch()
	{
		var storage = new InMemoryScreenshotStorage();
		var manager = new ScreenshotManager(storage);
		var config = BuildConfig();
		var key = new TestStep(config, "Journey", "01 Step");

		// Seed a green baseline.
		_ = manager.CompareWithBaselineAndDispose(new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0)), key, []);

		// Compare a red image against the green baseline.
		var result = manager.CompareWithBaselineAndDispose(new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0)), key, []);

		_ = result.Passed.Should().BeFalse();
		_ = result.ReportPath.Should().NotBeNull();
		_ = storage.BaselineExists(key).Should().BeTrue();
		_ = storage.NewScreenshotExists(key).Should().BeTrue();
		_ = storage.DiffImageExists(key).Should().BeTrue();
	}

	[Test]
	public void CompareWithBaselineAndDisposeFailsWithoutWritingWhenDimensionsDiffer()
	{
		var storage = new InMemoryScreenshotStorage();
		var manager = new ScreenshotManager(storage);
		var config = BuildConfig();
		var key = new TestStep(config, "Journey", "01 Step");

		_ = manager.CompareWithBaselineAndDispose(new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0)), key, []);

		var result = manager.CompareWithBaselineAndDispose(new Image<Rgb24>(80, 100, new Rgb24(0, 128, 0)), key, []);

		_ = result.Passed.Should().BeFalse();
		_ = result.PixelDiffPercentage.Should().Be(1);
		_ = storage.ListAllFiles(config, "Journey").Should().ContainSingle().Which.Should().Be("01 Step.png");
	}
}
