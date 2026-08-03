using System.Text;

namespace MobileJourneys.Tests;

/// <summary>
/// In-memory <see cref="ScreenshotStorage"/> for unit tests. Files live in nested
/// dictionaries keyed by platform display name, container path, and filename, using the
/// same <see cref="ArtifactNaming"/> layout as <see cref="FilesystemScreenshotStorage"/>
/// so the two backends produce the same observable behavior.
/// </summary>
internal sealed class InMemoryScreenshotStorage : ScreenshotStorage
{
	private readonly Dictionary<string, Dictionary<string, Dictionary<string, byte[]>>> _files = [];

	internal override bool BaselineExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.Container) is { } container
		&& container.ContainsKey(ArtifactNaming.BaselineFileName(testStep));

	internal override byte[] ReadBaseline(TestStep testStep)
	{
		var fileName = ArtifactNaming.BaselineFileName(testStep);
		return
			Lookup(testStep.Config, testStep.Container) is { } container
			&& container.TryGetValue(fileName, out var bytes)
			? bytes
			: throw new FileNotFoundException(
				$"No baseline '{fileName}' under '{testStep.Config.DisplayName}/{testStep.Container}'."
			);
	}

	internal override void WriteBaseline(TestStep testStep, byte[] pngBytes) =>
		GetOrCreateContainer(testStep.Config, testStep.Container)[ArtifactNaming.BaselineFileName(testStep)] = pngBytes;

	internal override void WriteNewScreenshot(TestStep testStep, byte[] pngBytes) =>
		GetOrCreateContainer(testStep.Config, testStep.Container)[ArtifactNaming.NewFileName(testStep)] = pngBytes;

	internal override byte[]? ReadNewScreenshot(TestStep testStep) =>
		Lookup(testStep.Config, testStep.Container) is { } container
		&& container.TryGetValue(ArtifactNaming.NewFileName(testStep), out var bytes)
			? bytes
			: null;

	internal override void WriteDiffImage(
		TestStep testStep,
		double pixelErrorPercentage,
		int pixelErrorCount,
		byte[] pngBytes
	) =>
		GetOrCreateContainer(testStep.Config, testStep.Container)[
			ArtifactNaming.DiffFileName(testStep, pixelErrorPercentage, pixelErrorCount)
		] = pngBytes;

	internal override void WriteFailScreenshot(TestStep testStep, string suffix, byte[] pngBytes) =>
		GetOrCreateContainer(testStep.Config, testStep.Container)[ArtifactNaming.FailFileName(testStep, suffix)] =
			pngBytes;

	internal override void WriteCrashLog(TestStep testStep, string content) =>
		GetOrCreateContainer(testStep.Config, testStep.Container)[ArtifactNaming.CrashLogFileName(testStep)] =
			Encoding.UTF8.GetBytes(content);

	protected override IReadOnlyList<StoredFile> ListFiles(PlatformConfig config) =>
		_files.TryGetValue(config.DisplayName, out var platform)
			?
			[
				.. platform.SelectMany(container =>
					container.Value.Keys.Where(n => !n.StartsWith('.')).Select(n => new StoredFile(container.Key, n))
				),
			]
			: [];

	internal override bool HasFailureArtifacts(PlatformConfig config, JourneyDefinition journey) =>
		journey.Containers.Any(container =>
			Lookup(config, container) is { } files
			&& files.Keys.Any(n => ArtifactNaming.IsFailureArtifactForJourney(n, journey.Name))
		);

	internal override void DeleteFailureArtifactsForStep(TestStep testStep)
	{
		if (Lookup(testStep.Config, testStep.Container) is not { } container)
		{
			return;
		}

		foreach (
			var name in container
				.Keys.Where(n => ArtifactNaming.IsFailureArtifactForStep(n, testStep.StepName, testStep.JourneyName))
				.ToList()
		)
		{
			_ = container.Remove(name);
		}
	}

	internal override void DeleteAllFailureArtifacts(PlatformConfig config, JourneyDefinition journey)
	{
		foreach (var containerPath in journey.Containers)
		{
			if (Lookup(config, containerPath) is not { } container)
			{
				continue;
			}

			foreach (
				var name in container
					.Keys.Where(n => ArtifactNaming.IsFailureArtifactForJourney(n, journey.Name))
					.ToList()
			)
			{
				_ = container.Remove(name);
			}
		}
	}

	internal override byte[]? ReadFile(PlatformConfig config, string container, string fileName) =>
		Lookup(config, container) is { } files && files.TryGetValue(fileName, out var bytes) ? bytes : null;

	internal override string FileVersion(PlatformConfig config, string container, string fileName) =>
		Lookup(config, container) is { } files && files.TryGetValue(fileName, out var bytes)
			? bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
			: string.Empty;

	protected override void DeleteFile(PlatformConfig config, StoredFile file)
	{
		if (Lookup(config, file.Container) is { } container)
		{
			_ = container.Remove(file.FileName);
		}
	}

	protected override void DeleteEmptyContainers(PlatformConfig config)
	{
		if (!_files.TryGetValue(config.DisplayName, out var platform))
		{
			return;
		}

		foreach (var containerPath in platform.Where(c => c.Value.Count == 0).Select(c => c.Key).ToList())
		{
			_ = platform.Remove(containerPath);
		}
	}

	// --- Test-only helpers (not on ScreenshotStorage) ---

	/// <summary>Returns <c>true</c> when a <c>.new</c> capture exists for the given step.</summary>
	internal bool NewScreenshotExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.Container) is { } container
		&& container.ContainsKey(ArtifactNaming.NewFileName(testStep));

	/// <summary>Returns <c>true</c> when a diff image exists for the given step (any percentage).</summary>
	internal bool DiffImageExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.Container) is { } container
		&& container.Keys.Any(n =>
			n.StartsWith($"{testStep.StepName} [{testStep.JourneyName}]_diff_", StringComparison.Ordinal)
		);

	/// <summary>Returns <c>true</c> when a FAIL screenshot exists for the given step (any suffix).</summary>
	internal bool FailScreenshotExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.Container) is { } container
		&& container.Keys.Any(n =>
			n.StartsWith($"{testStep.StepName} [{testStep.JourneyName}]_FAIL_", StringComparison.Ordinal)
		);

	/// <summary>Returns <c>true</c> when a crash log exists for the given step.</summary>
	internal bool CrashLogExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.Container) is { } container
		&& container.ContainsKey(ArtifactNaming.CrashLogFileName(testStep));

	/// <summary>Returns the raw bytes stored for any artifact name (test introspection only).</summary>
	internal byte[] ReadRaw(PlatformConfig config, string container, string fileName) =>
		Lookup(config, container) is { } files && files.TryGetValue(fileName, out var bytes)
			? bytes
			: throw new FileNotFoundException($"No artifact '{fileName}' under '{config.DisplayName}/{container}'.");

	/// <summary>Lists every stored file name under the container (test introspection only).</summary>
	internal IReadOnlyList<string> ListAllFiles(PlatformConfig config, string container) =>
		Lookup(config, container) is { } files ? [.. files.Keys] : [];

	private Dictionary<string, byte[]>? Lookup(PlatformConfig config, string container) =>
		_files.TryGetValue(config.DisplayName, out var platform) && platform.TryGetValue(container, out var files)
			? files
			: null;

	private Dictionary<string, byte[]> GetOrCreateContainer(PlatformConfig config, string container)
	{
		if (!_files.TryGetValue(config.DisplayName, out var platform))
		{
			platform = [];
			_files[config.DisplayName] = platform;
		}

		if (!platform.TryGetValue(container, out var files))
		{
			files = [];
			platform[container] = files;
		}

		return files;
	}
}
