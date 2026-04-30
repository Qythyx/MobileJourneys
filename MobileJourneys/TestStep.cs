namespace MobileJourneys;

/// <summary>
/// Identifies a single step's screenshots within a <see cref="ScreenshotStorage"/>:
/// the platform fixture, the journey container, and the numbered step name (without
/// extension) used as the baseline filename stem (e.g., <c>"02 Tap HamburgerMenu"</c>).
/// </summary>
/// <param name="Config">Platform fixture identifying the screenshot subdirectory.</param>
/// <param name="JourneyName">Journey container under which the step's artifacts live.</param>
/// <param name="StepName">Numbered step name without extension.</param>
public sealed record TestStep(PlatformConfig Config, string JourneyName, string StepName);
