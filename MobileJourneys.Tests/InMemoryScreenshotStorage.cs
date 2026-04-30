using System.Text;

namespace MobileJourneys.Tests;

/// <summary>
/// In-memory <see cref="ScreenshotStorage"/> for unit tests. Files live in nested
/// dictionaries keyed by platform display name, journey name, and the implementation's
/// own filename layout. Filename construction mirrors <see cref="FilesystemScreenshotStorage"/>
/// so the two backends produce the same observable behavior.
/// </summary>
internal sealed class InMemoryScreenshotStorage : ScreenshotStorage
{
	private const string BaselineExtension = ".png";
	private const string NewSuffix = ".new.png";
	private const string DiffPrefix = "_diff_";
	private const string DiffExtension = ".png";
	private const string FailPrefix = "_FAIL_";
	private const string FailExtension = ".png";
	private const string CrashLogExtension = ".CRASH.txt";

	private readonly Dictionary<string, Dictionary<string, Dictionary<string, byte[]>>> _files = [];

	internal override bool BaselineExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.JourneyName) is { } journey
		&& journey.ContainsKey(testStep.StepName + BaselineExtension);

	internal override byte[] ReadBaseline(TestStep testStep)
	{
		var fileName = testStep.StepName + BaselineExtension;
		return
			Lookup(testStep.Config, testStep.JourneyName) is { } journey && journey.TryGetValue(fileName, out var bytes)
			? bytes
			: throw new FileNotFoundException(
				$"No baseline '{fileName}' for journey '{testStep.JourneyName}' on '{testStep.Config.DisplayName}'."
			);
	}

	internal override void WriteBaseline(TestStep testStep, byte[] pngBytes) =>
		GetOrCreateJourney(testStep.Config, testStep.JourneyName)[testStep.StepName + BaselineExtension] = pngBytes;

	internal override void WriteNewScreenshot(TestStep testStep, byte[] pngBytes) =>
		GetOrCreateJourney(testStep.Config, testStep.JourneyName)[testStep.StepName + NewSuffix] = pngBytes;

	internal override void WriteDiffImage(TestStep testStep, double pixelErrorPercentage, byte[] pngBytes) =>
		GetOrCreateJourney(testStep.Config, testStep.JourneyName)[
			DiffFileName(testStep.StepName, pixelErrorPercentage)
		] = pngBytes;

	internal override void WriteFailScreenshot(TestStep testStep, string suffix, byte[] pngBytes) =>
		GetOrCreateJourney(testStep.Config, testStep.JourneyName)[FailFileName(testStep.StepName, suffix)] = pngBytes;

	internal override void WriteCrashLog(TestStep testStep, string content) =>
		GetOrCreateJourney(testStep.Config, testStep.JourneyName)[testStep.StepName + CrashLogExtension] =
			Encoding.UTF8.GetBytes(content);

	protected override IReadOnlyList<string> ListJourneysInStorage(PlatformConfig config) =>
		_files.TryGetValue(config.DisplayName, out var platform) ? [.. platform.Keys] : [];

	protected override IReadOnlyList<string> ListBaselineStepNames(PlatformConfig config, string journeyName) =>
		Lookup(config, journeyName) is { } journey
			? [.. journey.Keys.Where(IsBaselineFileName).Select(n => n[..^BaselineExtension.Length])]
			: [];

	internal override bool HasFailureArtifacts(PlatformConfig config, JourneyDefinition journey) =>
		Lookup(config, journey.Name) is { } dict && dict.Keys.Any(IsFailureArtifact);

	internal override void DeleteFailureArtifactsForStep(TestStep testStep)
	{
		if (Lookup(testStep.Config, testStep.JourneyName) is not { } journey)
		{
			return;
		}

		foreach (var name in journey.Keys.Where(n => IsFailureArtifactForStep(n, testStep.StepName)).ToList())
		{
			_ = journey.Remove(name);
		}
	}

	internal override void DeleteAllFailureArtifacts(PlatformConfig config, JourneyDefinition journey)
	{
		if (Lookup(config, journey.Name) is not { } dict)
		{
			return;
		}

		foreach (var name in dict.Keys.Where(IsFailureArtifact).ToList())
		{
			_ = dict.Remove(name);
		}
	}

	protected override void DeleteBaseline(TestStep testStep)
	{
		if (Lookup(testStep.Config, testStep.JourneyName) is { } journey)
		{
			_ = journey.Remove(testStep.StepName + BaselineExtension);
		}
	}

	protected override void DeleteJourney(PlatformConfig config, string journeyName)
	{
		if (_files.TryGetValue(config.DisplayName, out var platform))
		{
			_ = platform.Remove(journeyName);
		}
	}

	protected override void DeleteJourneyIfEmpty(PlatformConfig config, string journeyName)
	{
		if (
			_files.TryGetValue(config.DisplayName, out var platform)
			&& platform.TryGetValue(journeyName, out var journey)
			&& journey.Count == 0
		)
		{
			_ = platform.Remove(journeyName);
		}
	}

	// --- Test-only helpers (not on ScreenshotStorage) ---

	/// <summary>Returns <c>true</c> when a <c>.new</c> capture exists for the given step.</summary>
	internal bool NewScreenshotExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.JourneyName) is { } journey
		&& journey.ContainsKey(testStep.StepName + NewSuffix);

	/// <summary>Returns <c>true</c> when a diff image exists for the given step (any percentage).</summary>
	internal bool DiffImageExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.JourneyName) is { } journey
		&& journey.Keys.Any(n =>
			n.StartsWith(testStep.StepName + DiffPrefix, StringComparison.Ordinal)
			&& n.EndsWith(DiffExtension, StringComparison.Ordinal)
		);

	/// <summary>Returns <c>true</c> when a FAIL screenshot exists for the given step (any suffix).</summary>
	internal bool FailScreenshotExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.JourneyName) is { } journey
		&& journey.Keys.Any(n =>
			n.StartsWith(testStep.StepName + FailPrefix, StringComparison.Ordinal)
			&& n.EndsWith(FailExtension, StringComparison.Ordinal)
		);

	/// <summary>Returns <c>true</c> when a crash log exists for the given step.</summary>
	internal bool CrashLogExists(TestStep testStep) =>
		Lookup(testStep.Config, testStep.JourneyName) is { } journey
		&& journey.ContainsKey(testStep.StepName + CrashLogExtension);

	/// <summary>Returns the raw bytes stored for any artifact name (test introspection only).</summary>
	internal byte[] ReadRaw(PlatformConfig config, string journeyName, string fileName) =>
		Lookup(config, journeyName) is { } journey && journey.TryGetValue(fileName, out var bytes)
			? bytes
			: throw new FileNotFoundException($"No artifact '{fileName}' under '{config.DisplayName}/{journeyName}'.");

	/// <summary>Lists every stored file name under the journey (test introspection only).</summary>
	internal IReadOnlyList<string> ListAllFiles(PlatformConfig config, string journeyName) =>
		Lookup(config, journeyName) is { } journey ? [.. journey.Keys] : [];

	private Dictionary<string, byte[]>? Lookup(PlatformConfig config, string journeyName) =>
		_files.TryGetValue(config.DisplayName, out var platform) && platform.TryGetValue(journeyName, out var journey)
			? journey
			: null;

	private Dictionary<string, byte[]> GetOrCreateJourney(PlatformConfig config, string journeyName)
	{
		if (!_files.TryGetValue(config.DisplayName, out var platform))
		{
			platform = [];
			_files[config.DisplayName] = platform;
		}

		if (!platform.TryGetValue(journeyName, out var journey))
		{
			journey = [];
			platform[journeyName] = journey;
		}

		return journey;
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
}
