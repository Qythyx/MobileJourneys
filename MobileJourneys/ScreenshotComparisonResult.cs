namespace MobileJourneys;

public sealed record ScreenshotComparisonResult(bool Passed, double PixelDiffPercentage, string? ScreenshotDir);
