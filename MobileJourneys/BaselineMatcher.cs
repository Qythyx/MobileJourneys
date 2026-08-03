using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;

namespace MobileJourneys;

/// <summary>
/// A step's baseline, held decoded so that polling for a match pays the decode once rather than
/// once per capture. Obtained from <see cref="ScreenshotManager.TryLoadBaseline"/>, which returns
/// <c>null</c> for a step that has no baseline yet. Owned by the caller for the length of one
/// step's polling, so fixtures running in parallel share nothing.
/// </summary>
/// <param name="baseline">The decoded baseline image, disposed with this instance.</param>
/// <param name="testStep">The step the baseline belongs to; supplies the fixture's thresholds.</param>
public sealed class BaselineMatcher(Image baseline, TestStep testStep) : IDisposable
{
	/// <summary>Whether an image matches the baseline within the fixture's pixel budget.</summary>
	/// <param name="actual">The screenshot image at full device resolution.</param>
	/// <param name="maskRegions">Regions to exclude from comparison, in the image's pixel coordinates.</param>
	public bool Matches(Image<Rgb24> actual, Rectangle[] maskRegions) =>
		actual.Width == baseline.Width
		&& actual.Height == baseline.Height
		&& ScreenshotManager
			.CalcDiff(actual, baseline, testStep, ScreenshotManager.EffectiveMasks(baseline, maskRegions))
			.PixelErrorPercentage <= testStep.Config.MaxDiffPixelPercentage;

	/// <inheritdoc/>
	public void Dispose() => baseline.Dispose();
}
