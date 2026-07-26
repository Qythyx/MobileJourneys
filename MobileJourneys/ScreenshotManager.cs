using Codeuctivity.ImageSharpCompare;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;

namespace MobileJourneys;

/// <summary>
/// Image capture and pixel-diff comparison for screenshot baselines. Routes I/O through
/// <see cref="ScreenshotStorage"/>. Pure image-processing utilities (scaling, masking,
/// stability checks) live in <see cref="ImageHelpers"/>.
/// </summary>
/// <param name="storage">Where baselines and failure artifacts are read/written.</param>
public sealed class ScreenshotManager(ScreenshotStorage storage)
{
	/// <summary>
	/// Suffix for the zero-byte FAIL marker written when the actual screenshot's dimensions differ
	/// from the baseline's. A pixel diff is impossible across mismatched sizes, so the marker just
	/// names the reason in the filename.
	/// </summary>
	private const string SizeMismatchFailSuffix = "different size";

	/// <summary>
	/// Persists an already-captured FAIL screenshot via <see cref="ScreenshotStorage.WriteFailScreenshot"/>.
	/// The caller captures the image itself so it can do so the instant the step fails, before any
	/// slower diagnostics run — otherwise a timing failure's evidence shows a screen that finished
	/// rendering in the meantime, contradicting the failure it is supposed to document.
	/// </summary>
	/// <param name="testStep">Identifies the step the FAIL screenshot belongs to.</param>
	/// <param name="suffix">Suffix appended after <c>_FAIL_</c> (e.g., <c>"CRASH"</c> or a sanitized exception message). The caller is responsible for sanitizing the suffix for filename use.</param>
	/// <param name="pngBytes">The screenshot captured at the moment of failure; empty when the app was unreachable.</param>
	/// <param name="details">Full failure text stored in the image's metadata, so the viewer can show
	/// the whole message rather than the truncated, sanitized version the filename can hold.</param>
	/// <returns>Display path to the saved file (suitable for log messages).</returns>
	public string WriteFailScreenshot(TestStep testStep, string suffix, byte[] pngBytes, string details)
	{
		if (pngBytes.Length == 0)
		{
			storage.WriteFailScreenshot(testStep, suffix, pngBytes);
			return storage.GetReportPath(testStep);
		}

		using var image = Image.Load<Rgb24>(pngBytes);
		ImageHelpers.SetFailureDetails(image, details);
		storage.WriteFailScreenshot(testStep, suffix, ToPngBytes(image));
		return storage.GetReportPath(testStep);
	}

	/// <summary>
	/// Compares an image against a baseline at full resolution and disposes it. Mask regions are
	/// in the image's pixel coordinates. When a baseline is first written, the mask regions are
	/// stored in the baseline PNG's metadata; on later comparisons they are unioned with the live
	/// regions so content that shifts size between runs stays masked in both images.
	/// </summary>
	/// <param name="actual">The screenshot image at full device resolution.</param>
	/// <param name="testStep">Identifies the step whose baseline the image is compared against.</param>
	/// <param name="maskRegions">Regions to exclude from comparison, in the image's pixel coordinates.</param>
	public ScreenshotComparisonResult CompareWithBaselineAndDispose(
		Image<Rgb24> actual,
		TestStep testStep,
		Rectangle[] maskRegions
	)
	{
		using (actual)
		{
			if (!storage.BaselineExists(testStep))
			{
				ImageHelpers.SetMaskMetadata(actual, maskRegions);
				storage.WriteBaseline(testStep, ToPngBytes(actual));
				return new(true, 0, null);
			}

			using var baseline = Image.Load(storage.ReadBaseline(testStep));

			if (actual.Width != baseline.Width || actual.Height != baseline.Height)
			{
				storage.WriteNewScreenshot(testStep, ToPngBytes(actual));
				storage.WriteFailScreenshot(testStep, SizeMismatchFailSuffix, []);
				return new(false, 100, storage.GetReportPath(testStep));
			}

			// The live mask comes from the actual image; union it with the baseline's own mask
			// (stored when the baseline was written) so content that differs in size between the
			// two images stays masked in both.
			var baselineMasks = ImageHelpers.GetMaskMetadata(baseline);
			var effectiveMasks = baselineMasks.Length > 0 ? [.. maskRegions, .. baselineMasks] : maskRegions;

			ICompareResult diff;
			if (effectiveMasks is { Length: > 0 })
			{
				using var mask = ImageHelpers.CreateExclusionMask(actual.Width, actual.Height, effectiveMasks);
				diff = ImageSharpCompare.CalcDiff(
					actual,
					baseline,
					mask,
					pixelColorShiftTolerance: testStep.Config.ColorTolerance
				);
			}
			else
			{
				diff = ImageSharpCompare.CalcDiff(
					actual,
					baseline,
					pixelColorShiftTolerance: testStep.Config.ColorTolerance
				);
			}

			var passed = diff.PixelErrorPercentage == 0;
			if (!passed)
			{
				storage.WriteNewScreenshot(testStep, ToPngBytes(actual));

				using var diffImage = ImageSharpCompare.CalcDiffMaskImage(
					actual,
					baseline,
					pixelColorShiftTolerance: testStep.Config.ColorTolerance
				);
				ImageHelpers.RecolorDiff(diffImage, effectiveMasks);
				storage.WriteDiffImage(testStep, diff.PixelErrorPercentage, ToPngBytes((Image<Rgb24>)diffImage));
			}

			return new(passed, diff.PixelErrorPercentage, passed ? null : storage.GetReportPath(testStep));
		}
	}

	public void DeleteFailureArtifactsForStep(TestStep testStep) => storage.DeleteFailureArtifactsForStep(testStep);

	public void DeleteAllFailureArtifacts(PlatformConfig config, JourneyDefinition journey) =>
		storage.DeleteAllFailureArtifacts(config, journey);

	public bool HasFailureArtifacts(PlatformConfig config, JourneyDefinition journey) =>
		storage.HasFailureArtifacts(config, journey);

	public void WriteCrashLog(TestStep testStep, string content) => storage.WriteCrashLog(testStep, content);

	private static byte[] ToPngBytes(Image<Rgb24> image)
	{
		using var stream = new MemoryStream();
		image.SaveAsPng(stream);
		return stream.ToArray();
	}
}
