using AwesomeAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class ScreenshotHelperTests
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

		var scaled = ScreenshotHelper.ScaleMaskRegions(regions, from: new Size(1000, 2000), to: new Size(500, 1000));

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

		var scaled = ScreenshotHelper.ScaleMaskRegions(regions, from: new Size(100, 100), to: new Size(50, 50));

		_ = scaled.Should().BeSameAs(regions);
	}

	[Test]
	public void ScaleMaskRegionsReturnsInputUnchangedWhenSourceWidthIsZero()
	{
		// Defensive case: from-size of (0, …) would div-by-zero; method bails out.
		var regions = new[] { new Rectangle(10, 10, 20, 20) };

		var scaled = ScreenshotHelper.ScaleMaskRegions(regions, from: new Size(0, 100), to: new Size(50, 50));

		_ = scaled.Should().BeSameAs(regions);
	}

	[Test]
	public void AreImagesStableReturnsTrueForIdenticalImages()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));

		_ = ScreenshotHelper.AreImagesStable(a, b, []).Should().BeTrue();
	}

	[Test]
	public void AreImagesStableReturnsFalseForDifferentImagesWithoutMask()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(100, 100, new Rgb24(0, 255, 0));

		_ = ScreenshotHelper.AreImagesStable(a, b, []).Should().BeFalse();
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

		_ = ScreenshotHelper.AreImagesStable(a, b, mask).Should().BeTrue();
	}

	[Test]
	public void AreImagesStableReturnsFalseWhenDifferenceIsOutsideMask()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));

		// Differ at (50,50); mask covers (10,10,10×10) elsewhere.
		b.ProcessPixelRows(accessor => accessor.GetRowSpan(50)[50] = new Rgb24(0, 255, 0));

		var mask = new[] { new Rectangle(10, 10, 10, 10) };

		_ = ScreenshotHelper.AreImagesStable(a, b, mask).Should().BeFalse();
	}

	[Test]
	public void AreImagesStableThrowsArgumentExceptionWhenDimensionsDiffer()
	{
		using var a = new Image<Rgb24>(100, 100, new Rgb24(255, 0, 0));
		using var b = new Image<Rgb24>(101, 100, new Rgb24(255, 0, 0));

		Action act = () => ScreenshotHelper.AreImagesStable(a, b, []);
		_ = act.Should().Throw<ArgumentException>().WithMessage("*same size*");
	}
}
