namespace MobileJourneys;

/// <summary>What comparing a freshly captured screenshot against its step's baseline found.</summary>
public enum ScreenshotOutcome
{
	/// <summary>The capture matched the baseline within the fixture's tolerance.</summary>
	Matched,

	/// <summary>The capture differed from the baseline, and was kept as a new capture.</summary>
	Differed,

	/// <summary>
	/// The step has no baseline yet. The capture was kept as a new capture, which someone has to
	/// accept before it becomes the baseline later runs are compared against.
	/// </summary>
	NoBaseline,
}

/// <summary>The outcome of comparing a freshly captured screenshot against its baseline.</summary>
/// <param name="Outcome">What the comparison found.</param>
/// <param name="PixelDiffPercentage">Percentage of pixels that differ between actual and baseline; 0 when there was no baseline.</param>
/// <param name="ReportPath">Display path of the journey container where the failure artifacts were written; <c>null</c> when nothing was written.</param>
public sealed record ScreenshotComparisonResult(
	ScreenshotOutcome Outcome,
	double PixelDiffPercentage,
	string? ReportPath
);
