using MobileUITests.Framework;
using Codeuctivity.ImageSharpCompare;
using OpenQA.Selenium.Appium;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;

namespace MobileUITests;

public static class ScreenshotHelper
{
	private const int MaxHeightPixels = 2000;

	public static string CaptureScreenshot(
		AppiumDriver driver,
		PlatformConfig config,
		string journeyName,
		string stepName
	)
	{
		using var image = Image.Load<Rgb24>(driver.GetScreenshot().AsByteArray);
		image.ScaleToMax();
		var dir = GetScreenshotsDir(config, journeyName);
		_ = Directory.CreateDirectory(dir);
		var filePath = Path.Combine(dir, $"{stepName}.png");
		image.SaveAsPng(filePath);
		return filePath;
	}

	/// <summary>
	/// Scales an image down and compares it against a baseline. Accepts an unscaled
	/// (full-resolution) image and mask regions in the image's pixel coordinates.
	/// Both image and mask regions are scaled down proportionally for baseline comparison,
	/// and the image is disposed.
	/// </summary>
	/// <param name="actual">The unscaled screenshot image at full device resolution.</param>
	/// <param name="config">Platform configuration for baseline directory selection.</param>
	/// <param name="journeyName">Subfolder within the screenshots directory.</param>
	/// <param name="stepName">The screenshot name (without extension).</param>
	/// <param name="maskRegions">Regions to exclude from comparison, in the image's pixel coordinates.</param>
	public static ScreenshotComparisonResult CompareWithBaselineAndDispose(
		Image<Rgb24> actual,
		PlatformConfig config,
		string journeyName,
		string stepName,
		Rectangle[] maskRegions
	)
	{
		using (actual)
		{
			var sourceSize = new System.Drawing.Size(actual.Size.Width, actual.Size.Height);
			actual.ScaleToMax();
			var targetSize = new System.Drawing.Size(actual.Size.Width, actual.Size.Height);
			maskRegions = ScaleMaskRegions(maskRegions, sourceSize, targetSize);

			var baselineDir = GetScreenshotsDir(config, journeyName);
			var baselinePath = Path.Combine(baselineDir, $"{stepName}.png");

			if (!File.Exists(baselinePath))
			{
				_ = Directory.CreateDirectory(baselineDir);
				actual.SaveAsPng(baselinePath);
				return new(true, 0, null);
			}

			using var baseline = Image.Load(baselinePath);

			if (actual.Width != baseline.Width || actual.Height != baseline.Height)
			{
				return new(false, 1, null);
			}

			ICompareResult diff;
			if (maskRegions is { Length: > 0 })
			{
				using var mask = CreateExclusionMask(actual.Width, actual.Height, maskRegions);
				diff = ImageSharpCompare.CalcDiff(
					actual,
					baseline,
					mask,
					pixelColorShiftTolerance: config.ColorTolerance
				);
			}
			else
			{
				diff = ImageSharpCompare.CalcDiff(actual, baseline, pixelColorShiftTolerance: config.ColorTolerance);
			}

			var passed = diff.PixelErrorPercentage == 0;
			string? actualPath = null;
			string? diffPath = null;
			if (!passed)
			{
				actualPath = Path.Combine(baselineDir, $"{stepName}.new.png");
				actual.SaveAsPng(actualPath);

				diffPath = Path.Combine(baselineDir, $"{stepName}_diff_{diff.PixelErrorPercentage:F3}%.png");
				using var diffImage = ImageSharpCompare.CalcDiffMaskImage(
					actual,
					baseline,
					pixelColorShiftTolerance: config.ColorTolerance
				);
				RecolorDiffBackground(diffImage, maskRegions);
				diffImage.SaveAsPng(diffPath);
			}

			return new(passed, diff.PixelErrorPercentage, baselineDir);
		}
	}

	internal static void ScaleToMax(this Image<Rgb24> image)
	{
		if (image.Height > MaxHeightPixels)
		{
			var scale = (double)MaxHeightPixels / image.Height;
			image.Mutate(x => x.Resize((int)(image.Width * scale), MaxHeightPixels));
		}
	}

	/// <summary>Decodes a PNG screenshot into a Image.</summary>
	/// <param name="screenshot">The screenshot.</param>
	/// <param name="scale">Whether to scale the image down to the maximum height.</param>
	internal static Image<Rgb24> AsImage(this OpenQA.Selenium.Screenshot screenshot, bool scale)
	{
		var image = Image.Load<Rgb24>(screenshot.AsByteArray);
		if (scale)
		{
			image.ScaleToMax();
		}
		return image;
	}

	/// <summary>Scales mask regions from one coordinate space to another.</summary>
	/// <param name="regions">Mask regions in the <paramref name="from"/> coordinate space.</param>
	/// <param name="from">The size of the coordinate space the regions are currently in.</param>
	/// <param name="to">The size of the coordinate space to scale to.</param>
	internal static Rectangle[] ScaleMaskRegions(Rectangle[] regions, System.Drawing.Size from, System.Drawing.Size to)
	{
		if (regions.Length == 0 || from.Width <= 0)
		{
			return regions;
		}

		var scaleX = (double)to.Width / from.Width;
		var scaleY = (double)to.Height / from.Height;
		return
		[
			.. regions.Select(r => new Rectangle(
				(int)(r.X * scaleX),
				(int)(r.Y * scaleY),
				(int)(r.Width * scaleX),
				(int)(r.Height * scaleY)
			)),
		];
	}

	/// <summary>
	/// Compares two images pixel-by-pixel, skipping pixels that fall within mask regions.
	/// Uses fast row-span comparison for unmasked rows and per-pixel checks for masked rows.
	/// Mask regions must be in pixel coordinates.
	/// </summary>
	/// <param name="a">The first image.</param>
	/// <param name="b">The second image.</param>
	/// <param name="maskRegions">Regions to skip during comparison, in pixel coordinates.</param>
	internal static bool AreImagesStable(Image<Rgb24> a, Image<Rgb24> b, Rectangle[] maskRegions)
	{
		static bool RowIntersectsMask(int y, Rectangle[] regions)
		{
			foreach (var r in regions)
			{
				if (y >= r.Y && y < r.Y + r.Height)
				{
					return true;
				}
			}

			return false;
		}

		if (a.Width != b.Width || a.Height != b.Height)
		{
			return false;
		}

		var bytesPerPixel = a.PixelType.BitsPerPixel / 8;
		var stable = true;
		a.ProcessPixelRows(
			b,
			(accessorA, accessorB) =>
			{
				for (var y = 0; y < accessorA.Height; y++)
				{
					var rowA = System.Runtime.InteropServices.MemoryMarshal.AsBytes(accessorA.GetRowSpan(y));
					var rowB = System.Runtime.InteropServices.MemoryMarshal.AsBytes(accessorB.GetRowSpan(y));

					if (!RowIntersectsMask(y, maskRegions))
					{
						if (!rowA.SequenceEqual(rowB))
						{
							stable = false;
							return;
						}

						continue;
					}

					// Row intersects at least one mask region — compare pixel-by-pixel, skipping masked pixels.
					for (var x = 0; x < accessorA.Width; x++)
					{
						if (IsInMaskRegion(x, y, maskRegions))
						{
							continue;
						}

						var offset = x * bytesPerPixel;
						if (!rowA.Slice(offset, bytesPerPixel).SequenceEqual(rowB.Slice(offset, bytesPerPixel)))
						{
							stable = false;
							return;
						}
					}
				}
			}
		);
		return stable;
	}

	private static void RecolorDiffBackground(Image diffImage, Rectangle[]? maskRegions)
	{
		var black = new Rgb24(0, 0, 0);
		var background = new Rgb24(0xBB, 0xBB, 0x88);
		const float tintStrength = 0.25f;
		((Image<Rgb24>)diffImage).ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height; y++)
			{
				var row = accessor.GetRowSpan(y);
				for (var x = 0; x < row.Length; x++)
				{
					if (maskRegions is { Length: > 0 } && IsInMaskRegion(x, y, maskRegions))
					{
						var pixel = row[x];
						row[x] = new Rgb24(
							(byte)Math.Min(255, pixel.R + ((255 - pixel.R) * tintStrength)),
							(byte)(pixel.G * (1 - tintStrength)),
							(byte)(pixel.B * (1 - tintStrength))
						);
					}
					else if (row[x] == black)
					{
						row[x] = background;
					}
				}
			}
		});
	}

	private static bool IsInMaskRegion(int x, int y, Rectangle[] regions)
	{
		foreach (var region in regions)
		{
			if (x >= region.X && x < region.X + region.Width && y >= region.Y && y < region.Y + region.Height)
			{
				return true;
			}
		}

		return false;
	}

	private static Image<Rgb24> CreateExclusionMask(int width, int height, Rectangle[] regions)
	{
		var mask = new Image<Rgb24>(width, height, new Rgb24(0, 0, 0));

		mask.ProcessPixelRows(accessor =>
		{
			foreach (var region in regions)
			{
				var top = Math.Max(0, region.Y);
				var bottom = Math.Min(height, region.Y + region.Height);
				var left = Math.Max(0, region.X);
				var right = Math.Min(width, region.X + region.Width);

				for (var y = top; y < bottom; y++)
				{
					var row = accessor.GetRowSpan(y);
					row[left..right].Fill(new Rgb24(255, 255, 255));
				}
			}
		});

		return mask;
	}

	public static readonly string ScreenshotsRootDir = Path.Combine(TestAssembly.ProjectRootPath, "Screenshots");

	public static string GetScreenshotsDir(PlatformConfig config) =>
		Path.Combine(ScreenshotsRootDir, config.DisplayName);

	public static string GetScreenshotsDir(PlatformConfig config, string journeyName) =>
		Path.Combine(GetScreenshotsDir(config), journeyName);
}
