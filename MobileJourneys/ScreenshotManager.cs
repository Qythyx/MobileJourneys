using Codeuctivity.ImageSharpCompare;
using OpenQA.Selenium.Appium;
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
	/// <summary>Captures a FAIL screenshot (taken when a step throws) at full resolution and persists it via <see cref="ScreenshotStorage.WriteFailScreenshot"/>.</summary>
	/// <param name="driver">The Appium driver to capture from.</param>
	/// <param name="testStep">Identifies the step the FAIL screenshot belongs to.</param>
	/// <param name="suffix">Suffix appended after <c>_FAIL_</c> (e.g., <c>"CRASH"</c> or a sanitized exception message). The caller is responsible for sanitizing the suffix for filename use.</param>
	/// <returns>Display path to the saved file (suitable for log messages).</returns>
	public string CaptureFailScreenshot(AppiumDriver driver, TestStep testStep, string suffix)
	{
		using var image = Image.Load<Rgb24>(driver.GetScreenshot().AsByteArray);
		storage.WriteFailScreenshot(testStep, suffix, ToPngBytes(image));
		return storage.GetReportPath(testStep.Config, testStep.JourneyName);
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
				return new(false, 1, null);
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

			return new(
				passed,
				diff.PixelErrorPercentage,
				passed ? null : storage.GetReportPath(testStep.Config, testStep.JourneyName)
			);
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
