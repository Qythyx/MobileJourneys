namespace MobileJourneys;

/// <summary>
/// Identifies a single step's screenshots within a <see cref="ScreenshotStorage"/>:
/// the platform fixture, the container the step's screenshots live in, the numbered step
/// name used as the baseline filename stem (e.g., <c>"02 Tap HamburgerMenu"</c>), and the
/// journey being run — stamped into failure-artifact filenames so artifacts in a container
/// shared by several journeys stay attributable to the run that produced them.
/// </summary>
/// <param name="Config">Platform fixture identifying the screenshot subdirectory.</param>
/// <param name="Container">'/'-separated container path (relative to the platform folder)
/// under which the step's screenshots live. Equals the journey name for flat journeys; a
/// nested node path (e.g., <c>"Home/Menu"</c>) for tree-defined journeys.</param>
/// <param name="StepName">Numbered step name without extension.</param>
/// <param name="JourneyName">Name of the journey being run, used to attribute failure artifacts.</param>
public sealed record TestStep(PlatformConfig Config, string Container, string StepName, string JourneyName);
