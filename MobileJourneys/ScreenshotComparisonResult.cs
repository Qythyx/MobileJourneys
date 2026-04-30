namespace MobileJourneys;

/// <summary>The outcome of comparing a freshly captured screenshot against its baseline.</summary>
/// <param name="Passed"><c>true</c> when the pixel-error percentage is zero (or under the platform's color tolerance).</param>
/// <param name="PixelDiffPercentage">Percentage of pixels that differ between actual and baseline.</param>
/// <param name="ReportPath">Display path of the journey container where .new and _diff artifacts were written; <c>null</c> when nothing was written.</param>
public sealed record ScreenshotComparisonResult(bool Passed, double PixelDiffPercentage, string? ReportPath);
