using MobileJourneys.Framework;

namespace MobileJourneys;

/// <summary>
/// Default <see cref="ScreenshotStorage"/> implementation that persists artifacts under
/// the consumer's <c>Screenshots/</c> directory on disk.
/// </summary>
/// <param name="rootDir">Absolute path to the screenshots root (e.g., <c>&lt;project&gt;/Screenshots</c>).</param>
internal sealed class FilesystemScreenshotStorage(string rootDir) : ScreenshotStorage
{
	private const string BaselineExtension = ".png";
	private const string NewSuffix = ".new.png";
	private const string DiffPrefix = "_diff_";
	private const string DiffExtension = ".png";
	private const string FailPrefix = "_FAIL_";
	private const string FailExtension = ".png";
	private const string CrashLogExtension = ".CRASH.txt";

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
		WriteBytes(testStep.Config, testStep.JourneyName, testStep.StepName + BaselineExtension, pngBytes);

	/// <inheritdoc/>
	internal override void WriteNewScreenshot(TestStep testStep, byte[] pngBytes) =>
		WriteBytes(testStep.Config, testStep.JourneyName, testStep.StepName + NewSuffix, pngBytes);

	/// <inheritdoc/>
	internal override void WriteDiffImage(TestStep testStep, double pixelErrorPercentage, byte[] pngBytes) =>
		WriteBytes(
			testStep.Config,
			testStep.JourneyName,
			DiffFileName(testStep.StepName, pixelErrorPercentage),
			pngBytes
		);

	/// <inheritdoc/>
	internal override void WriteFailScreenshot(TestStep testStep, string suffix, byte[] pngBytes) =>
		WriteBytes(testStep.Config, testStep.JourneyName, FailFileName(testStep.StepName, suffix), pngBytes);

	/// <inheritdoc/>
	internal override void WriteCrashLog(TestStep testStep, string content)
	{
		var dir = JourneyDir(testStep.Config, testStep.JourneyName);
		_ = Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, testStep.StepName + CrashLogExtension), content);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<string> ListJourneysInStorage(PlatformConfig config)
	{
		var platformDir = PlatformDir(config);
		return Directory.Exists(platformDir)
			? [.. Directory.GetDirectories(platformDir).Select(Path.GetFileName).Where(n => n is not null)!]
			: [];
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<string> ListBaselineStepNames(PlatformConfig config, string journeyName)
	{
		var dir = JourneyDir(config, journeyName);
		return Directory.Exists(dir)
			?
			[
				.. Directory
					.GetFiles(dir, "*" + BaselineExtension)
					.Select(Path.GetFileName)
					.Where(n => n is not null && IsBaselineFileName(n))
					.Select(n => n![..^BaselineExtension.Length]),
			]
			: [];
	}

	/// <inheritdoc/>
	internal override bool HasFailureArtifacts(PlatformConfig config, JourneyDefinition journey)
	{
		var dir = JourneyDir(config, journey.Name);
		return Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any(p => IsFailureArtifact(Path.GetFileName(p)));
	}

	/// <inheritdoc/>
	internal override void DeleteFailureArtifactsForStep(TestStep testStep)
	{
		var dir = JourneyDir(testStep.Config, testStep.JourneyName);
		if (!Directory.Exists(dir))
		{
			return;
		}

		foreach (var path in Directory.GetFiles(dir))
		{
			var name = Path.GetFileName(path);
			if (IsFailureArtifactForStep(name, testStep.StepName))
			{
				File.Delete(path);
			}
		}
	}

	/// <inheritdoc/>
	internal override void DeleteAllFailureArtifacts(PlatformConfig config, JourneyDefinition journey)
	{
		var dir = JourneyDir(config, journey.Name);
		if (!Directory.Exists(dir))
		{
			return;
		}

		foreach (var path in Directory.GetFiles(dir))
		{
			if (IsFailureArtifact(Path.GetFileName(path)))
			{
				File.Delete(path);
			}
		}
	}

	/// <inheritdoc/>
	protected override void DeleteBaseline(TestStep testStep)
	{
		var path = BaselinePath(testStep);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	/// <inheritdoc/>
	protected override void DeleteJourney(PlatformConfig config, string journeyName)
	{
		var dir = JourneyDir(config, journeyName);
		if (Directory.Exists(dir))
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	/// <inheritdoc/>
	protected override void DeleteJourneyIfEmpty(PlatformConfig config, string journeyName)
	{
		var dir = JourneyDir(config, journeyName);
		if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
		{
			Directory.Delete(dir);
		}
	}

	private static string DiffFileName(string stepName, double pixelErrorPercentage) =>
		$"{stepName}{DiffPrefix}{pixelErrorPercentage:F3}%{DiffExtension}";

	private static string FailFileName(string stepName, string suffix) =>
		$"{stepName}{FailPrefix}{suffix}{FailExtension}";

	private static bool IsBaselineFileName(string fileName) =>
		!fileName.StartsWith('.')
		&& fileName.EndsWith(BaselineExtension, StringComparison.Ordinal)
		&& !fileName.EndsWith(NewSuffix, StringComparison.Ordinal)
		&& !fileName.Contains(DiffPrefix, StringComparison.Ordinal)
		&& !fileName.Contains(FailPrefix, StringComparison.Ordinal);

	private static bool IsFailureArtifact(string fileName) =>
		fileName.EndsWith(NewSuffix, StringComparison.Ordinal)
		|| fileName.Contains(DiffPrefix, StringComparison.Ordinal)
		|| fileName.Contains(FailPrefix, StringComparison.Ordinal)
		|| fileName.EndsWith(CrashLogExtension, StringComparison.Ordinal);

	private static bool IsFailureArtifactForStep(string fileName, string stepName) =>
		fileName.Equals(stepName + NewSuffix, StringComparison.Ordinal)
		|| fileName.StartsWith(stepName + DiffPrefix, StringComparison.Ordinal)
		|| fileName.StartsWith(stepName + FailPrefix, StringComparison.Ordinal)
		|| fileName.Equals(stepName + CrashLogExtension, StringComparison.Ordinal);

	private void WriteBytes(PlatformConfig config, string journeyName, string fileName, byte[] bytes)
	{
		var dir = JourneyDir(config, journeyName);
		_ = Directory.CreateDirectory(dir);
		File.WriteAllBytes(Path.Combine(dir, fileName), bytes);
	}

	private string PlatformDir(PlatformConfig config) => Path.Combine(RootDir, config.DisplayName);

	private string JourneyDir(PlatformConfig config, string journeyName) =>
		Path.Combine(PlatformDir(config), journeyName);

	private string BaselinePath(TestStep testStep) =>
		Path.Combine(JourneyDir(testStep.Config, testStep.JourneyName), testStep.StepName + BaselineExtension);
}
