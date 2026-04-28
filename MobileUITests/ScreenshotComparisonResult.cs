namespace MobileUITests;

public sealed record ScreenshotComparisonResult(bool Passed, double PixelDiffPercentage, string? ScreenshotDir);
