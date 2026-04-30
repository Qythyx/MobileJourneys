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
	/// <summary>Captures a FAIL screenshot (taken when a step throws), scales it to <see cref="PlatformConfig.MaxScreenshotHeight"/>, and persists it via <see cref="ScreenshotStorage.WriteFailScreenshot"/>.</summary>
	/// <param name="driver">The Appium driver to capture from.</param>
	/// <param name="testStep">Identifies the step the FAIL screenshot belongs to.</param>
	/// <param name="suffix">Suffix appended after <c>_FAIL_</c> (e.g., <c>"CRASH"</c> or a sanitized exception message). The caller is responsible for sanitizing the suffix for filename use.</param>
	/// <returns>Display path to the saved file (suitable for log messages).</returns>
	public string CaptureFailScreenshot(AppiumDriver driver, TestStep testStep, string suffix)
	{
		using var image = Image.Load<Rgb24>(driver.GetScreenshot().AsByteArray);
		image.ScaleToMax(testStep.Config.MaxScreenshotHeight);
		storage.WriteFailScreenshot(testStep, suffix, ToPngBytes(image));
		return storage.GetReportPath(testStep.Config, testStep.JourneyName);
	}

	/// <summary>
	/// Scales an image down and compares it against a baseline. Accepts an unscaled
	/// (full-resolution) image and mask regions in the image's pixel coordinates.
	/// Both image and mask regions are scaled down proportionally for baseline comparison,
	/// and the image is disposed.
	/// </summary>
	/// <param name="actual">The unscaled screenshot image at full device resolution.</param>
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
			var sourceSize = new System.Drawing.Size(actual.Size.Width, actual.Size.Height);
			actual.ScaleToMax(testStep.Config.MaxScreenshotHeight);
			var targetSize = new System.Drawing.Size(actual.Size.Width, actual.Size.Height);
			maskRegions = ImageHelpers.ScaleMaskRegions(maskRegions, sourceSize, targetSize);

			if (!storage.BaselineExists(testStep))
			{
				storage.WriteBaseline(testStep, ToPngBytes(actual));
				return new(true, 0, null);
			}

			using var baseline = Image.Load(storage.ReadBaseline(testStep));

			if (actual.Width != baseline.Width || actual.Height != baseline.Height)
			{
				return new(false, 1, null);
			}

			ICompareResult diff;
			if (maskRegions is { Length: > 0 })
			{
				using var mask = ImageHelpers.CreateExclusionMask(actual.Width, actual.Height, maskRegions);
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
				ImageHelpers.RecolorDiff(diffImage, maskRegions);
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
