using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;

namespace MobileJourneys;

/// <summary>
/// Pure image-processing utilities used by <see cref="ScreenshotManager"/> and
/// <see cref="TestDriver"/>: scaling, mask-region transforms, masked pixel comparison,
/// diff-image recoloring, and exclusion-mask generation. Lives in a static class so
/// extension methods (<see cref="ScaleToMax"/>, <see cref="AsImage"/>) work.
/// </summary>
internal static class ImageHelpers
{
	internal static void ScaleToMax(this Image<Rgb24> image, int maxHeight)
	{
		if (image.Height > maxHeight)
		{
			var scale = (double)maxHeight / image.Height;
			image.Mutate(x => x.Resize((int)(image.Width * scale), maxHeight));
		}
	}

	/// <summary>Decodes a PNG screenshot into an Image.</summary>
	/// <param name="screenshot">The screenshot.</param>
	internal static Image<Rgb24> AsImage(this OpenQA.Selenium.Screenshot screenshot) =>
		Image.Load<Rgb24>(screenshot.AsByteArray);

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
				(int)Math.Ceiling(r.Width * scaleX),
				(int)Math.Ceiling(r.Height * scaleY)
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
	internal static bool AreImagesEqual(Image<Rgb24> a, Image<Rgb24> b, Rectangle[] maskRegions)
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
			throw new ArgumentException("Images must be the same size");
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

	internal static void RecolorDiff(Image diffImage, Rectangle[]? maskRegions)
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

	internal static Image<Rgb24> CreateExclusionMask(int width, int height, Rectangle[] regions)
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
}
