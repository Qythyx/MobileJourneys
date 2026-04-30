namespace MobileJourneys;

/// <summary>
/// Persistence layer for screenshot baselines and journey artifacts (failure
/// screenshots, diff images, crash logs). Step-scoped operations identify their
/// target via <see cref="TestStep"/>. Backends own the filename layout — the
/// extension/suffix for baselines, "new" captures, diff images, FAIL screenshots,
/// and crash logs is internal to the implementation and not part of this contract.
/// </summary>
public abstract class ScreenshotStorage
{
	/// <summary>Returns <c>true</c> if a baseline exists for the given step.</summary>
	internal abstract bool BaselineExists(TestStep testStep);

	/// <summary>Reads the bytes of an existing baseline.</summary>
	/// <exception cref="FileNotFoundException">The baseline does not exist.</exception>
	internal abstract byte[] ReadBaseline(TestStep testStep);

	/// <summary>Writes the baseline PNG for a step, creating containers as needed.</summary>
	internal abstract void WriteBaseline(TestStep testStep, byte[] pngBytes);

	/// <summary>Writes the "actual" PNG captured during a baseline mismatch (the <c>.new</c> companion).</summary>
	internal abstract void WriteNewScreenshot(TestStep testStep, byte[] pngBytes);

	/// <summary>Writes the diff visualization image for a baseline mismatch, tagged with the pixel-error percentage.</summary>
	internal abstract void WriteDiffImage(TestStep testStep, double pixelErrorPercentage, byte[] pngBytes);

	/// <summary>Writes a FAIL-screenshot PNG (used when a step throws). The <paramref name="suffix"/> is appended after <c>_FAIL_</c>; the caller must sanitize it for use in a filename.</summary>
	internal abstract void WriteFailScreenshot(TestStep testStep, string suffix, byte[] pngBytes);

	/// <summary>Writes a UTF-8 crash-log artifact for a step.</summary>
	internal abstract void WriteCrashLog(TestStep testStep, string content);

	/// <summary>Lists the journey container names that exist for the platform. Empty when the platform container is missing. The returned list is a snapshot — safe to mutate the storage during enumeration.</summary>
	protected abstract IReadOnlyList<string> ListJourneysInStorage(PlatformConfig config);

	/// <summary>Lists the step names (prefix without extension) for which a baseline exists. Excludes failure artifacts and dotfiles. Empty when the journey is missing.</summary>
	protected abstract IReadOnlyList<string> ListBaselineStepNames(PlatformConfig config, string journeyName);

	/// <summary>Returns <c>true</c> when the journey contains any failure artifact (new/diff/FAIL screenshot, or crash log).</summary>
	internal abstract bool HasFailureArtifacts(PlatformConfig config, JourneyDefinition journey);

	/// <summary>Deletes every failure artifact associated with one step (matched by step-name prefix). No-op when the journey is missing.</summary>
	internal abstract void DeleteFailureArtifactsForStep(TestStep testStep);

	/// <summary>Deletes every failure artifact in the journey, leaving baselines intact. No-op when the journey is missing.</summary>
	internal abstract void DeleteAllFailureArtifacts(PlatformConfig config, JourneyDefinition journey);

	/// <summary>Deletes the baseline for a step. No-op when the file or journey is missing.</summary>
	protected abstract void DeleteBaseline(TestStep testStep);

	/// <summary>Deletes the journey container and everything in it. No-op when missing.</summary>
	protected abstract void DeleteJourney(PlatformConfig config, string journeyName);

	/// <summary>Deletes the journey container only when it has no files.</summary>
	protected abstract void DeleteJourneyIfEmpty(PlatformConfig config, string journeyName);

	/// <summary>Returns a display string identifying a journey container (used in failure messages and the extraneous-files report).</summary>
	public virtual string GetReportPath(PlatformConfig config, string journeyName) =>
		$"{config.DisplayName}/{journeyName}/";

	/// <summary>Returns a display string identifying a single step's baseline (used in the extraneous-files report).</summary>
	protected virtual string GetReportPath(TestStep testStep) =>
		$"{testStep.Config.DisplayName}/{testStep.JourneyName}/{testStep.StepName}.png";

	/// <summary>
	/// Finds journey containers and baselines that aren't expected by <paramref name="config"/>.
	/// The expected baseline names per journey are produced by <paramref name="stepNamesOf"/>.
	/// </summary>
	/// <param name="config">Framework configuration providing the platform and journey lists.</param>
	/// <param name="stepNamesOf">Returns the expected step names (without extension) for a given journey.</param>
	/// <param name="deleteExtraneous">When <c>true</c>, deletes the extraneous baselines and
	/// empty journey containers after collecting them.</param>
	/// <returns>Display paths suitable for the user-facing extraneous-files report.</returns>
	internal List<string> FindExtraneous(
		FrameworkConfig config,
		Func<JourneyDefinition, IEnumerable<string>> stepNamesOf,
		bool deleteExtraneous
	)
	{
		var expectedSteps = config
			.PlatformConfigs.SelectMany(p =>
				config.Journeys.SelectMany(j => stepNamesOf(j).Select(name => new TestStep(p, j.Name, name)))
			)
			.ToHashSet();
		var expectedJourneys = expectedSteps.Select(s => (s.Config, s.JourneyName)).ToHashSet();

		var extraneousJourneys = new List<(PlatformConfig Platform, string JourneyName)>();
		var extraneousBaselines = new List<TestStep>();

		foreach (var platform in config.PlatformConfigs)
		{
			foreach (var journeyName in ListJourneysInStorage(platform))
			{
				if (!expectedJourneys.Contains((platform, journeyName)))
				{
					extraneousJourneys.Add((platform, journeyName));
					continue;
				}

				foreach (var stepName in ListBaselineStepNames(platform, journeyName))
				{
					var step = new TestStep(platform, journeyName, stepName);
					if (!expectedSteps.Contains(step))
					{
						extraneousBaselines.Add(step);
					}
				}
			}
		}

		if (deleteExtraneous)
		{
			foreach (var (platform, journeyName) in extraneousJourneys)
			{
				DeleteJourney(platform, journeyName);
			}

			foreach (var step in extraneousBaselines)
			{
				DeleteBaseline(step);
			}

			foreach (var platform in config.PlatformConfigs)
			{
				foreach (var journeyName in ListJourneysInStorage(platform))
				{
					DeleteJourneyIfEmpty(platform, journeyName);
				}
			}
		}

		return
		[
			.. extraneousJourneys.Select(j => GetReportPath(j.Platform, j.JourneyName)),
			.. extraneousBaselines.Select(GetReportPath),
		];
	}
}
