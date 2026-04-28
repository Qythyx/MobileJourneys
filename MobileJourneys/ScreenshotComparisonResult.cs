namespace MobileJourneys;

/// <summary>The outcome of comparing a freshly captured screenshot against its baseline.</summary>
/// <param name="Passed"><c>true</c> when the pixel-error percentage is zero (or under the platform's color tolerance).</param>
/// <param name="PixelDiffPercentage">Percentage of pixels that differ between actual and baseline.</param>
/// <param name="ScreenshotDir">Directory where the .new and _diff artifacts were written; <c>null</c> when no comparison happened.</param>
public sealed record ScreenshotComparisonResult(bool Passed, double PixelDiffPercentage, string? ScreenshotDir);
