namespace MobileJourneys;

/// <summary>
/// Filename layout shared by <see cref="ScreenshotStorage"/> backends: the baseline
/// extension, the suffix patterns for failure artifacts (new capture, diff image, FAIL
/// screenshot, crash log), and the <c>" [journey]"</c> attribution stamped into failure
/// artifacts so artifacts in a container shared by several journeys stay attributable —
/// and cleanable — per journey.
/// </summary>
internal static class ArtifactNaming
{
	internal const string BaselineExtension = ".png";
	private const string NewSuffix = ".new.png";
	private const string DiffPrefix = "_diff_";
	private const string DiffExtension = ".png";
	private const string FailPrefix = "_FAIL_";
	private const string FailExtension = ".png";
	private const string CrashLogExtension = ".CRASH.txt";

	/// <summary>Baseline filename for a step.</summary>
	/// <param name="testStep">The step the baseline belongs to.</param>
	internal static string BaselineFileName(TestStep testStep) => testStep.StepName + BaselineExtension;

	/// <summary>Filename for the "actual" capture written on a baseline mismatch.</summary>
	/// <param name="testStep">The step the capture belongs to.</param>
	internal static string NewFileName(TestStep testStep) => Attributed(testStep) + NewSuffix;

	/// <summary>Filename for the diff visualization image, tagged with the pixel-error percentage.</summary>
	/// <param name="testStep">The step the diff belongs to.</param>
	/// <param name="pixelErrorPercentage">Percentage of differing pixels, embedded in the filename.</param>
	internal static string DiffFileName(TestStep testStep, double pixelErrorPercentage) =>
		$"{Attributed(testStep)}{DiffPrefix}{pixelErrorPercentage:F3}%{DiffExtension}";

	/// <summary>Filename for a FAIL screenshot (written when a step throws).</summary>
	/// <param name="testStep">The step the screenshot belongs to.</param>
	/// <param name="suffix">Sanitized reason appended after <c>_FAIL_</c>.</param>
	internal static string FailFileName(TestStep testStep, string suffix) =>
		$"{Attributed(testStep)}{FailPrefix}{suffix}{FailExtension}";

	/// <summary>Filename for a step's crash-log artifact.</summary>
	/// <param name="testStep">The step the crash log belongs to.</param>
	internal static string CrashLogFileName(TestStep testStep) => Attributed(testStep) + CrashLogExtension;

	/// <summary>Returns <c>true</c> when the filename is a baseline (not a dotfile or failure artifact).</summary>
	/// <param name="fileName">The filename to classify.</param>
	internal static bool IsBaseline(string fileName) =>
		!fileName.StartsWith('.')
		&& fileName.EndsWith(BaselineExtension, StringComparison.Ordinal)
		&& !IsFailureArtifact(fileName);

	/// <summary>Strips the baseline extension, returning the step-name stem.</summary>
	/// <param name="baselineFileName">A filename for which <see cref="IsBaseline"/> is <c>true</c>.</param>
	internal static string BaselineStepName(string baselineFileName) => baselineFileName[..^BaselineExtension.Length];

	/// <summary>Returns <c>true</c> when the filename is any failure artifact (new/diff/FAIL screenshot, or crash log).</summary>
	/// <param name="fileName">The filename to classify.</param>
	internal static bool IsFailureArtifact(string fileName) =>
		fileName.EndsWith(NewSuffix, StringComparison.Ordinal)
		|| fileName.Contains(DiffPrefix, StringComparison.Ordinal)
		|| fileName.Contains(FailPrefix, StringComparison.Ordinal)
		|| fileName.EndsWith(CrashLogExtension, StringComparison.Ordinal);

	/// <summary>Returns <c>true</c> when the filename is a failure artifact for the given step and journey.</summary>
	/// <param name="fileName">The filename to classify.</param>
	/// <param name="stepName">The step's numbered name (without extension).</param>
	/// <param name="journeyName">The journey the artifact must be attributed to.</param>
	internal static bool IsFailureArtifactForStep(string fileName, string stepName, string journeyName)
	{
		var attributed = Attributed(stepName, journeyName);
		return fileName.Equals(attributed + NewSuffix, StringComparison.Ordinal)
			|| fileName.StartsWith(attributed + DiffPrefix, StringComparison.Ordinal)
			|| fileName.StartsWith(attributed + FailPrefix, StringComparison.Ordinal)
			|| fileName.Equals(attributed + CrashLogExtension, StringComparison.Ordinal);
	}

	/// <summary>Returns <c>true</c> when the filename is a failure artifact attributed to the given journey.</summary>
	/// <param name="fileName">The filename to classify.</param>
	/// <param name="journeyName">The journey the artifact must be attributed to.</param>
	internal static bool IsFailureArtifactForJourney(string fileName, string journeyName) =>
		IsFailureArtifact(fileName) && fileName.Contains($" [{journeyName}]", StringComparison.Ordinal);

	/// <summary>A failure artifact's filename decomposed into its parts.</summary>
	/// <param name="StepName">The step's numbered name (without extension).</param>
	/// <param name="JourneyName">The journey the artifact is attributed to.</param>
	/// <param name="Kind">The artifact kind: <c>"new"</c>, <c>"diff"</c>, <c>"fail"</c>, or <c>"crash"</c>.</param>
	/// <param name="DiffPercent">Pixel-error percentage parsed from a diff image's filename; <c>null</c> for other kinds.</param>
	internal sealed record ParsedFailureArtifact(string StepName, string JourneyName, string Kind, double? DiffPercent);

	/// <summary>Parses a failure artifact's filename into its step, journey, kind, and diff percentage. Returns <c>null</c> when the filename is not an attributed failure artifact.</summary>
	/// <param name="fileName">The filename to parse.</param>
	internal static ParsedFailureArtifact? ParseFailureArtifact(string fileName)
	{
		var open = fileName.IndexOf(" [", StringComparison.Ordinal);
		if (open < 0)
		{
			return null;
		}

		var close = fileName.IndexOf(']', open);
		if (close < 0)
		{
			return null;
		}

		var stepName = fileName[..open];
		var journeyName = fileName[(open + 2)..close];
		var suffix = fileName[(close + 1)..];
		if (suffix == NewSuffix)
		{
			return new(stepName, journeyName, "new", null);
		}

		if (suffix == CrashLogExtension)
		{
			return new(stepName, journeyName, "crash", null);
		}

		if (suffix.StartsWith(DiffPrefix, StringComparison.Ordinal))
		{
			var percentText = suffix[DiffPrefix.Length..];
			var percentEnd = percentText.IndexOf('%');
			return new(
				stepName,
				journeyName,
				"diff",
				percentEnd > 0
				&& double.TryParse(
					percentText[..percentEnd],
					System.Globalization.CultureInfo.InvariantCulture,
					out var percent
				)
					? percent
					: null
			);
		}

		return suffix.StartsWith(FailPrefix, StringComparison.Ordinal)
			? new(stepName, journeyName, "fail", null)
			: null;
	}

	private static string Attributed(TestStep testStep) => Attributed(testStep.StepName, testStep.JourneyName);

	private static string Attributed(string stepName, string journeyName) => $"{stepName} [{journeyName}]";
}
