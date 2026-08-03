namespace MobileJourneys;

/// <summary>
/// Persistence layer for screenshot baselines and journey artifacts (failure screenshots,
/// diff images, crash logs). Step-scoped operations identify their target via
/// <see cref="TestStep"/>, whose container path locates the step's folder — the journey's
/// own folder for flat journeys, a shared node path for tree-defined journeys. Failure
/// artifacts are attributed to the journey that produced them, so several journeys can
/// fail on a shared step without clobbering each other's evidence.
/// </summary>
public abstract class ScreenshotStorage
{
	/// <summary>A stored file, identified by its container path and filename.</summary>
	/// <param name="Container">'/'-separated container path relative to the platform folder.</param>
	/// <param name="FileName">Filename within the container.</param>
	protected sealed record StoredFile(string Container, string FileName);

	/// <summary>Returns <c>true</c> if a baseline exists for the given step.</summary>
	internal abstract bool BaselineExists(TestStep testStep);

	/// <summary>Reads the bytes of an existing baseline.</summary>
	/// <exception cref="FileNotFoundException">The baseline does not exist.</exception>
	internal abstract byte[] ReadBaseline(TestStep testStep);

	/// <summary>Writes the baseline PNG for a step, creating containers as needed.</summary>
	internal abstract void WriteBaseline(TestStep testStep, byte[] pngBytes);

	/// <summary>Writes the "actual" PNG captured during a baseline mismatch (the <c>.new</c> companion).</summary>
	internal abstract void WriteNewScreenshot(TestStep testStep, byte[] pngBytes);

	/// <summary>Reads the bytes of a step's <c>.new</c> capture, or <c>null</c> when none exists.</summary>
	internal abstract byte[]? ReadNewScreenshot(TestStep testStep);

	/// <summary>Writes the diff visualization image for a baseline mismatch, tagged with the pixel-error percentage.</summary>
	internal abstract void WriteDiffImage(
		TestStep testStep,
		double pixelErrorPercentage,
		int pixelErrorCount,
		byte[] pngBytes
	);

	/// <summary>Writes a FAIL-screenshot PNG (used when a step throws). The <paramref name="suffix"/> is appended after <c>_FAIL_</c>; the caller must sanitize it for use in a filename.</summary>
	internal abstract void WriteFailScreenshot(TestStep testStep, string suffix, byte[] pngBytes);

	/// <summary>Writes a UTF-8 crash-log artifact for a step.</summary>
	internal abstract void WriteCrashLog(TestStep testStep, string content);

	/// <summary>Lists every stored file under the platform, recursively. Excludes dotfiles. Empty when the platform container is missing. The returned list is a snapshot — safe to mutate the storage during enumeration.</summary>
	protected abstract IReadOnlyList<StoredFile> ListFiles(PlatformConfig config);

	/// <summary>Returns <c>true</c> when any of the journey's containers holds a failure artifact attributed to it.</summary>
	internal abstract bool HasFailureArtifacts(PlatformConfig config, JourneyDefinition journey);

	/// <summary>Deletes every failure artifact one step's journey produced for it. No-op when the container is missing.</summary>
	internal abstract void DeleteFailureArtifactsForStep(TestStep testStep);

	/// <summary>Deletes every failure artifact attributed to the journey, leaving baselines intact. No-op when the journey's containers are missing.</summary>
	internal abstract void DeleteAllFailureArtifacts(PlatformConfig config, JourneyDefinition journey);

	/// <summary>Deletes one stored file. No-op when missing.</summary>
	protected abstract void DeleteFile(PlatformConfig config, StoredFile file);

	/// <summary>Deletes containers left without any files, recursively. The platform container itself is kept.</summary>
	protected abstract void DeleteEmptyContainers(PlatformConfig config);

	/// <summary>Returns a display string identifying a step's container (used in failure messages).</summary>
	public virtual string GetReportPath(TestStep testStep) => $"{testStep.Config.DisplayName}/{testStep.Container}/";

	/// <summary>Returns a display string identifying a single stored file (used in the extraneous-files report).</summary>
	protected virtual string GetReportPath(PlatformConfig config, StoredFile file) =>
		$"{config.DisplayName}/{file.Container}/{file.FileName}";

	/// <summary>
	/// Finds stored files that no journey in <paramref name="config"/> references: baselines
	/// without a matching step, and failure artifacts whose step or journey no longer exists.
	/// </summary>
	/// <param name="config">Framework configuration providing the platform and journey lists.</param>
	/// <param name="deleteExtraneous">When <c>true</c>, deletes the extraneous files and any
	/// containers left empty after collecting them.</param>
	/// <returns>Display paths suitable for the user-facing extraneous-files report.</returns>
	internal List<string> FindExtraneous(FrameworkConfig config, bool deleteExtraneous)
	{
		var expected = new ExpectedScreenshots(config.Journeys);
		var extraneous = new List<(PlatformConfig Platform, StoredFile File)>();
		foreach (var platform in config.PlatformConfigs)
		{
			extraneous.AddRange(
				ListFiles(platform)
					.Where(file => !expected.IsExpected(file.Container, file.FileName))
					.Select(file => (platform, file))
			);
		}

		if (deleteExtraneous)
		{
			foreach (var (platform, file) in extraneous)
			{
				DeleteFile(platform, file);
			}

			foreach (var platform in config.PlatformConfigs)
			{
				DeleteEmptyContainers(platform);
			}
		}

		return [.. extraneous.Select(e => GetReportPath(e.Platform, e.File)).Order(StringComparer.Ordinal)];
	}

	/// <summary>
	/// Locates every failure artifact attributed to the journey, so a reader can open the evidence.
	/// </summary>
	/// <param name="config">Platform fixture the journey ran on.</param>
	/// <param name="journey">The journey whose artifacts to find.</param>
	/// <returns>Locations in whatever form this backend can be addressed by, sorted.</returns>
	internal IReadOnlyList<string> FailureArtifactLocations(PlatformConfig config, JourneyDefinition journey) =>
		[
			.. ListFiles(config)
				.Where(file => ArtifactNaming.IsFailureArtifactForJourney(file.FileName, journey.Name))
				.Select(file => GetArtifactLocation(config, file))
				.Order(StringComparer.Ordinal),
		];

	/// <summary>
	/// Where a stored file can be found. Defaults to the same display path the reports use; a
	/// backend a reader can open directly overrides it with something they can act on.
	/// </summary>
	/// <param name="config">Platform fixture the file belongs to.</param>
	/// <param name="file">The stored file.</param>
	/// <returns>The location string.</returns>
	protected virtual string GetArtifactLocation(PlatformConfig config, StoredFile file) => GetReportPath(config, file);

	/// <summary>Lists every stored file under the platform as (container, filename) pairs — see <see cref="ListFiles"/>.</summary>
	internal IReadOnlyList<(string Container, string FileName)> ListStoredFiles(PlatformConfig config) =>
		[.. ListFiles(config).Select(f => (f.Container, f.FileName))];

	/// <summary>Deletes one stored file identified by container path and filename. No-op when missing.</summary>
	internal void DeleteStoredFile(PlatformConfig config, string container, string fileName) =>
		DeleteFile(config, new(container, fileName));

	/// <summary>Deletes containers left without any files — see <see cref="DeleteEmptyContainers"/>.</summary>
	internal void CleanupEmptyContainers(PlatformConfig config) => DeleteEmptyContainers(config);

	/// <summary>Reads the raw bytes of one stored file, or <c>null</c> when it does not exist.</summary>
	/// <param name="config">Platform fixture the file belongs to.</param>
	/// <param name="container">'/'-separated container path relative to the platform folder.</param>
	/// <param name="fileName">Filename within the container.</param>
	internal abstract byte[]? ReadFile(PlatformConfig config, string container, string fileName);

	/// <summary>
	/// A token that changes whenever the file's contents change and is stable while they don't,
	/// used to build cache-busting image URLs for the viewer. Empty when the file is missing.
	/// </summary>
	/// <param name="config">Platform fixture the file belongs to.</param>
	/// <param name="container">'/'-separated container path relative to the platform folder.</param>
	/// <param name="fileName">Filename within the container.</param>
	internal abstract string FileVersion(PlatformConfig config, string container, string fileName);
}
