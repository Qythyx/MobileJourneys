using MobileJourneys.Framework;

namespace MobileJourneys;

/// <summary>
/// Default <see cref="ScreenshotStorage"/> implementation that persists artifacts under
/// the consumer's <c>Screenshots/</c> directory on disk. Container paths map to nested
/// directories, so tree-defined journeys store shared steps once in a folder hierarchy
/// mirroring the tree.
/// </summary>
/// <param name="rootDir">Absolute path to the screenshots root (e.g., <c>&lt;project&gt;/Screenshots</c>).</param>
internal sealed class FilesystemScreenshotStorage(string rootDir) : ScreenshotStorage
{
	/// <summary>The screenshots root directory this instance writes under.</summary>
	internal string RootDir { get; } = rootDir;

	/// <summary>Builds a storage rooted at the consumer test project's <c>Screenshots/</c> directory.</summary>
	internal static FilesystemScreenshotStorage Default() =>
		new(Path.Combine(TestAssembly.ProjectRootPath, "Screenshots"));

	/// <inheritdoc/>
	internal override bool BaselineExists(TestStep testStep) => File.Exists(BaselinePath(testStep));

	/// <inheritdoc/>
	internal override byte[] ReadBaseline(TestStep testStep) => File.ReadAllBytes(BaselinePath(testStep));

	/// <inheritdoc/>
	internal override void WriteBaseline(TestStep testStep, byte[] pngBytes) =>
		WriteBytes(testStep, ArtifactNaming.BaselineFileName(testStep), pngBytes);

	/// <inheritdoc/>
	internal override void WriteNewScreenshot(TestStep testStep, byte[] pngBytes) =>
		WriteBytes(testStep, ArtifactNaming.NewFileName(testStep), pngBytes);

	/// <inheritdoc/>
	internal override byte[]? ReadNewScreenshot(TestStep testStep)
	{
		var path = Path.Combine(
			ContainerDir(testStep.Config, testStep.Container),
			ArtifactNaming.NewFileName(testStep)
		);
		return File.Exists(path) ? File.ReadAllBytes(path) : null;
	}

	/// <inheritdoc/>
	internal override void WriteDiffImage(
		TestStep testStep,
		double pixelErrorPercentage,
		int pixelErrorCount,
		byte[] pngBytes
	) => WriteBytes(testStep, ArtifactNaming.DiffFileName(testStep, pixelErrorPercentage, pixelErrorCount), pngBytes);

	/// <inheritdoc/>
	internal override void WriteFailScreenshot(TestStep testStep, string suffix, byte[] pngBytes) =>
		WriteBytes(testStep, ArtifactNaming.FailFileName(testStep, suffix), pngBytes);

	/// <inheritdoc/>
	internal override void WriteCrashLog(TestStep testStep, string content)
	{
		var dir = ContainerDir(testStep.Config, testStep.Container);
		_ = Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, ArtifactNaming.CrashLogFileName(testStep)), content);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<StoredFile> ListFiles(PlatformConfig config)
	{
		var platformDir = PlatformDir(config);
		return !Directory.Exists(platformDir)
			? []
			:
			[
				.. Directory
					.EnumerateFiles(platformDir, "*", SearchOption.AllDirectories)
					.Select(path => new StoredFile(
						Path.GetRelativePath(platformDir, Path.GetDirectoryName(path)!)
							.Replace(Path.DirectorySeparatorChar, '/'),
						Path.GetFileName(path)
					))
					.Where(f => !f.FileName.StartsWith('.')),
			];
	}

	/// <inheritdoc/>
	internal override bool HasFailureArtifacts(PlatformConfig config, JourneyDefinition journey) =>
		journey.Containers.Any(container =>
		{
			var dir = ContainerDir(config, container);
			return Directory.Exists(dir)
				&& Directory
					.EnumerateFiles(dir)
					.Any(p => ArtifactNaming.IsFailureArtifactForJourney(Path.GetFileName(p), journey.Name));
		});

	/// <inheritdoc/>
	internal override void DeleteFailureArtifactsForStep(TestStep testStep)
	{
		var dir = ContainerDir(testStep.Config, testStep.Container);
		if (!Directory.Exists(dir))
		{
			return;
		}

		foreach (var path in Directory.GetFiles(dir))
		{
			if (
				ArtifactNaming.IsFailureArtifactForStep(Path.GetFileName(path), testStep.StepName, testStep.JourneyName)
			)
			{
				File.Delete(path);
			}
		}
	}

	/// <inheritdoc/>
	internal override void DeleteAllFailureArtifacts(PlatformConfig config, JourneyDefinition journey)
	{
		foreach (var container in journey.Containers)
		{
			var dir = ContainerDir(config, container);
			if (!Directory.Exists(dir))
			{
				continue;
			}

			foreach (var path in Directory.GetFiles(dir))
			{
				if (ArtifactNaming.IsFailureArtifactForJourney(Path.GetFileName(path), journey.Name))
				{
					File.Delete(path);
				}
			}
		}
	}

	/// <inheritdoc/>
	internal override byte[]? ReadFile(PlatformConfig config, string container, string fileName)
	{
		var path = Path.Combine(ContainerDir(config, container), fileName);
		return File.Exists(path) ? File.ReadAllBytes(path) : null;
	}

	/// <inheritdoc/>
	internal override string FileVersion(PlatformConfig config, string container, string fileName)
	{
		var path = Path.Combine(ContainerDir(config, container), fileName);
		return File.Exists(path)
			? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
			: string.Empty;
	}

	/// <inheritdoc/>
	protected override void DeleteFile(PlatformConfig config, StoredFile file)
	{
		var path = Path.Combine(ContainerDir(config, file.Container), file.FileName);
		if (!IsWithinRoot(path))
		{
			throw new ArgumentException(
				$"Refusing to delete a path outside the screenshots root: {file.Container}/{file.FileName}",
				nameof(file)
			);
		}

		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	/// <inheritdoc/>
	protected override string GetArtifactLocation(PlatformConfig config, StoredFile file) =>
		Path.Combine(ContainerDir(config, file.Container), file.FileName);

	/// <inheritdoc/>
	protected override void DeleteEmptyContainers(PlatformConfig config)
	{
		var platformDir = PlatformDir(config);
		if (!Directory.Exists(platformDir))
		{
			return;
		}

		// Deepest-first so a directory whose only content was empty subdirectories is itself removed.
		foreach (
			var dir in Directory
				.GetDirectories(platformDir, "*", SearchOption.AllDirectories)
				.OrderByDescending(d => d.Length)
		)
		{
			if (Directory.GetFileSystemEntries(dir).Length == 0)
			{
				Directory.Delete(dir);
			}
		}
	}

	private void WriteBytes(TestStep testStep, string fileName, byte[] bytes)
	{
		var dir = ContainerDir(testStep.Config, testStep.Container);
		_ = Directory.CreateDirectory(dir);
		File.WriteAllBytes(Path.Combine(dir, fileName), bytes);
	}

	private string PlatformDir(PlatformConfig config) => Path.Combine(RootDir, config.DisplayName);

	private string ContainerDir(PlatformConfig config, string container) =>
		Path.Combine(PlatformDir(config), container.Replace('/', Path.DirectorySeparatorChar));

	/// <summary>Returns whether the resolved path stays inside <see cref="RootDir"/> — a guard against a
	/// crafted container/filename escaping the screenshots tree via <c>..</c> or an absolute segment.</summary>
	/// <param name="path">The candidate path to test.</param>
	private bool IsWithinRoot(string path) =>
		Path.GetFullPath(path)
			.StartsWith(Path.GetFullPath(RootDir) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

	private string BaselinePath(TestStep testStep) =>
		Path.Combine(ContainerDir(testStep.Config, testStep.Container), ArtifactNaming.BaselineFileName(testStep));
}
