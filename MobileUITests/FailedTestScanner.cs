namespace MobileUITests;

public static class FailedTestScanner
{
	private static readonly string[] ResultFilePatterns = ["*.new.png", "*_diff_*.png", "*_FAIL_*.png", "*.CRASH.txt"];

	public static bool IsFailedJourney(PlatformConfig config, JourneyDefinition journey)
	{
		var journeyDir = ScreenshotHelper.GetScreenshotsDir(config, journey.Name);
		return Directory.Exists(journeyDir)
			&& ResultFilePatterns.Any(pat => Directory.GetFiles(journeyDir, pat).Length > 0);
	}

	public static void CleanupStepResults(PlatformConfig config, string journeyName, string filePrefix)
	{
		var journeyDir = ScreenshotHelper.GetScreenshotsDir(config, journeyName);
		if (!Directory.Exists(journeyDir))
		{
			return;
		}

		foreach (var pattern in ResultFilePatterns)
		{
			foreach (var file in Directory.GetFiles(journeyDir, $"{filePrefix}{pattern}"))
			{
				File.Delete(file);
			}
		}
	}

	public static void CleanupResults(PlatformConfig config, string journeyName)
	{
		var journeyDir = ScreenshotHelper.GetScreenshotsDir(config, journeyName);
		if (!Directory.Exists(journeyDir))
		{
			return;
		}

		foreach (var pattern in ResultFilePatterns)
		{
			foreach (var file in Directory.GetFiles(journeyDir, pattern))
			{
				File.Delete(file);
			}
		}
	}

	/// <summary>
	/// Finds screenshot folders and baseline files that don't correspond to any journey or step
	/// in <paramref name="config"/>'s suite. These are leftovers from renamed or deleted journeys/steps.
	/// </summary>
	/// <param name="config">Framework configuration providing the journey and platform lists.</param>
	/// <param name="deleteExtraneous">When <c>true</c>, deletes the extraneous files and empty
	/// journey folders after collecting them.</param>
	/// <returns>Paths relative to the Screenshots directory.</returns>
	public static List<string> FindExtraneousScreenshots(FrameworkConfig config, bool deleteExtraneous)
	{
		var journeys = config.Journeys.ToList();
		var expectedJourneyNames = new HashSet<string>(journeys.Select(j => j.Name));

		var expectedFilesByJourney = new Dictionary<string, HashSet<string>>();
		foreach (var journey in journeys)
		{
			var files = new HashSet<string> { $"01 {journey.InitialName}.png" };
			for (var i = 0; i < journey.Steps.Length; i++)
			{
				_ = files.Add($"{i + 2:D2} {journey.Steps[i].Name}.png");
			}

			expectedFilesByJourney[journey.Name] = files;
		}

		var extraneousFolders = new List<string>();
		var extraneousFiles = new List<string>();

		foreach (var platform in config.PlatformConfigs)
		{
			var configDir = ScreenshotHelper.GetScreenshotsDir(platform);
			if (!Directory.Exists(configDir))
			{
				continue;
			}

			foreach (var journeyDir in Directory.GetDirectories(configDir))
			{
				var journeyName = Path.GetFileName(journeyDir);
				if (!expectedJourneyNames.Contains(journeyName))
				{
					extraneousFolders.Add(journeyDir);
					continue;
				}

				var expectedFiles = expectedFilesByJourney[journeyName];
				foreach (var file in Directory.GetFiles(journeyDir))
				{
					var fileName = Path.GetFileName(file);
					if (
						fileName.StartsWith('.')
						|| fileName.EndsWith(".new.png", StringComparison.Ordinal)
						|| fileName.Contains("_diff_", StringComparison.Ordinal)
						|| fileName.Contains("_FAIL_", StringComparison.Ordinal)
					)
					{
						continue;
					}

					if (!expectedFiles.Contains(fileName))
					{
						extraneousFiles.Add(file);
					}
				}
			}
		}

		if (deleteExtraneous)
		{
			foreach (var path in extraneousFolders)
			{
				Directory.Delete(path, true);
			}

			foreach (var path in extraneousFiles)
			{
				File.Delete(path);
			}

			// Clean up journey folders that are now empty after individual file deletions.
			foreach (var platform in config.PlatformConfigs)
			{
				var configDir = ScreenshotHelper.GetScreenshotsDir(platform);
				if (!Directory.Exists(configDir))
				{
					continue;
				}

				foreach (var journeyDir in Directory.GetDirectories(configDir))
				{
					if (Directory.GetFileSystemEntries(journeyDir).Length == 0)
					{
						Directory.Delete(journeyDir);
					}
				}
			}
		}

		var rootDir = ScreenshotHelper.ScreenshotsRootDir;
		return
		[
			.. extraneousFolders.Select(p => Path.GetRelativePath(rootDir, p) + Path.DirectorySeparatorChar),
			.. extraneousFiles.Select(p => Path.GetRelativePath(rootDir, p)),
		];
	}
}
