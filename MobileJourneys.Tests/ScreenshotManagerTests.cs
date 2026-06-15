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
	public void ScaleMaskRegionsExpandsToFullyContainRegionWhenScalingRoundsFractionally()
	{
		// Scaling 10→6 (×0.6): source pixel [1, 2) maps to target range [0.6, 1.2), touching
		// target pixel index 1. Flooring the origin and ceiling the size independently would yield
		// X=0, W=1 — covering only pixel 0 and leaving pixel 1 outside the mask.
		var regions = new[] { new Rectangle(1, 1, 1, 1) };

		var scaled = ImageHelpers.ScaleMaskRegions(regions, from: new Size(10, 10), to: new Size(6, 6));

		var r = scaled[0];
		_ = r.X.Should().BeLessThanOrEqualTo(0);
		_ = r.Y.Should().BeLessThanOrEqualTo(0);
		_ = (r.X + r.Width).Should().BeGreaterThanOrEqualTo(2);
		_ = (r.Y + r.Height).Should().BeGreaterThanOrEqualTo(2);
	}

	[Test]
	public void SetAndGetMaskMetadataRoundTripsThroughPng()
	{
		var regions = new[] { new Rectangle(10, 20, 30, 40), new Rectangle(5, 6, 7, 8) };

		using var image = new Image<Rgb24>(50, 50);
		ImageHelpers.SetMaskMetadata(image, regions);

		using var stream = new MemoryStream();
		image.SaveAsPng(stream);
		using var reloaded = Image.Load(stream.ToArray());

		_ = ImageHelpers.GetMaskMetadata(reloaded).Should().Equal(regions);
	}

	[Test]
	public void GetMaskMetadataReturnsEmptyWhenChunkAbsent()
	{
		using var image = new Image<Rgb24>(10, 10);
		using var stream = new MemoryStream();
		image.SaveAsPng(stream);
		using var reloaded = Image.Load(stream.ToArray());

		_ = ImageHelpers.GetMaskMetadata(reloaded).Should().BeEmpty();
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
		new("26.2", "iPhone", IsLightTheme: true, "com.example.app", "/unused");

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

	[Test]
	public void CompareWithBaselineAndDisposeMasksBaselineContentWiderThanTheLiveMask()
	{
		var storage = new InMemoryScreenshotStorage();
		var manager = new ScreenshotManager(storage);
		var config = BuildConfig();
		var key = new TestStep(config, "Journey", "01 Step");

		// Seed a baseline (green with a red stripe at x∈[20,40)) whose stored mask is a wide
		// band x∈[10,40) — simulating a run where the dynamic element's content was wide.
		var baseline = new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0));
		PaintColumns(baseline, fromX: 20, toX: 40, new Rgb24(255, 0, 0));
		_ = manager.CompareWithBaselineAndDispose(baseline, key, [new Rectangle(10, 0, 30, 100)]);

		// A later run: plain green, so it differs from the baseline only at x∈[20,40) — outside
		// the narrower live mask x∈[10,20) but inside the baseline's stored mask. The union keeps
		// it masked, so the comparison passes.
		var actual = new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0));
		var result = manager.CompareWithBaselineAndDispose(actual, key, [new Rectangle(10, 0, 10, 100)]);

		_ = result.Passed.Should().BeTrue();
	}

	[Test]
	public void CompareWithBaselineAndDisposeFailsWhenLiveMaskTooNarrowAndBaselineStoredNoMask()
	{
		var storage = new InMemoryScreenshotStorage();
		var manager = new ScreenshotManager(storage);
		var config = BuildConfig();
		var key = new TestStep(config, "Journey", "01 Step");

		// Same scenario, but the baseline carries no stored mask (older baseline / no masking on
		// the seeding run). Without a baseline mask to union, the narrow live mask can't cover the
		// baseline's wider content, so the difference is detected — documents the fallback.
		var baseline = new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0));
		PaintColumns(baseline, fromX: 20, toX: 40, new Rgb24(255, 0, 0));
		_ = manager.CompareWithBaselineAndDispose(baseline, key, []);

		var actual = new Image<Rgb24>(100, 100, new Rgb24(0, 128, 0));
		var result = manager.CompareWithBaselineAndDispose(actual, key, [new Rectangle(10, 0, 10, 100)]);

		_ = result.Passed.Should().BeFalse();
	}

	private static void PaintColumns(Image<Rgb24> image, int fromX, int toX, Rgb24 color) =>
		image.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height; y++)
			{
				var row = accessor.GetRowSpan(y);
				for (var x = fromX; x < toX; x++)
				{
					row[x] = color;
				}
			}
		});
}
