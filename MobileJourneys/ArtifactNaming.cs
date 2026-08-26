namespace MobileJourneys;

/// <summary>
/// Filename layout shared by <see cref="ScreenshotStorage"/> backends: the baseline
/// extension, the suffix patterns for failure artifacts (new capture, diff image, FAIL
/// screenshot, crash log, error text), and the <c>" [journey]"</c> attribution stamped into failure
/// artifacts so artifacts in a container shared by several journeys stay attributable —
/// and cleanable — per journey.
/// </summary>
internal static class ArtifactNaming
{
	internal const string BaselineExtension = ".png";
	private const string NewSuffix = ".new.png";
	private const string DiffPrefix = "_diff_";
	private const string PixelCountPrefix = "_";
	private const string PixelCountSuffix = "px";
	private const string DiffExtension = ".png";
	private const string FailPrefix = "_FAIL_";
	private const string FailExtension = ".png";
	private const string CrashLogExtension = ".CRASH.txt";
	private const string ErrorTextExtension = ".ERROR.txt";

	/// <summary>Baseline filename for a step.</summary>
	/// <param name="testStep">The step the baseline belongs to.</param>
	internal static string BaselineFileName(TestStep testStep) => testStep.StepName + BaselineExtension;

	/// <summary>Filename for the "actual" capture written on a baseline mismatch.</summary>
	/// <param name="testStep">The step the capture belongs to.</param>
	internal static string NewFileName(TestStep testStep) => Attributed(testStep) + NewSuffix;

	/// <summary>Filename for the diff visualization image, tagged with the pixel-error percentage and count.</summary>
	/// <param name="testStep">The step the diff belongs to.</param>
	/// <param name="pixelErrorPercentage">Percentage of differing pixels, embedded in the filename.</param>
	/// <param name="pixelErrorCount">
	/// Number of differing pixels. Carried alongside the percentage because a failure of a few pixels
	/// rounds to 0.000%, which reads as "nothing differs" and hides how near the budget the step ran.
	/// </param>
	internal static string DiffFileName(TestStep testStep, double pixelErrorPercentage, int pixelErrorCount) =>
		$"{Attributed(testStep)}{DiffPrefix}{pixelErrorPercentage:F3}%{PixelCountPrefix}{pixelErrorCount}{PixelCountSuffix}{DiffExtension}";

	/// <summary>Filename for a FAIL screenshot (written when a step throws).</summary>
	/// <param name="testStep">The step the screenshot belongs to.</param>
	/// <param name="suffix">Sanitized reason appended after <c>_FAIL_</c>.</param>
	internal static string FailFileName(TestStep testStep, string suffix) =>
		$"{Attributed(testStep)}{FailPrefix}{suffix}{FailExtension}";

	/// <summary>Filename for a step's crash-log artifact.</summary>
	/// <param name="testStep">The step the crash log belongs to.</param>
	internal static string CrashLogFileName(TestStep testStep) => Attributed(testStep) + CrashLogExtension;

	/// <summary>Filename for a step's failure text, written when there is no screenshot to carry it.</summary>
	/// <param name="testStep">The step the text belongs to.</param>
	internal static string ErrorTextFileName(TestStep testStep) => Attributed(testStep) + ErrorTextExtension;

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
		|| fileName.EndsWith(CrashLogExtension, StringComparison.Ordinal)
		|| fileName.EndsWith(ErrorTextExtension, StringComparison.Ordinal);

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
			|| fileName.Equals(attributed + CrashLogExtension, StringComparison.Ordinal)
			|| fileName.Equals(attributed + ErrorTextExtension, StringComparison.Ordinal);
	}

	/// <summary>Returns <c>true</c> when the filename is a failure artifact attributed to the given journey.</summary>
	/// <param name="fileName">The filename to classify.</param>
	/// <param name="journeyName">The journey the artifact must be attributed to.</param>
	internal static bool IsFailureArtifactForJourney(string fileName, string journeyName) =>
		IsFailureArtifact(fileName) && fileName.Contains($" [{journeyName}]", StringComparison.Ordinal);

	/// <summary>A failure artifact's filename decomposed into its parts.</summary>
	/// <param name="StepName">The step's numbered name (without extension).</param>
	/// <param name="JourneyName">The journey the artifact is attributed to.</param>
	/// <param name="Kind">The artifact kind: <c>"new"</c>, <c>"diff"</c>, <c>"fail"</c>, <c>"crash"</c>, or <c>"error"</c>.</param>
	/// <param name="DiffPercent">Pixel-error percentage parsed from a diff image's filename; <c>null</c> for other kinds.</param>
	/// <param name="DiffPixelCount">
	/// Number of differing pixels parsed from a diff image's filename. <c>null</c> for other kinds, and
	/// for diff images written before the count was recorded.
	/// </param>
	internal sealed record ParsedFailureArtifact(
		string StepName,
		string JourneyName,
		string Kind,
		double? DiffPercent,
		int? DiffPixelCount
	);

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
			return new(stepName, journeyName, "new", null, null);
		}

		if (suffix == CrashLogExtension)
		{
			return new(stepName, journeyName, "crash", null, null);
		}

		if (suffix == ErrorTextExtension)
		{
			return new(stepName, journeyName, "error", null, null);
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
					: null,
				ParsePixelCount(percentText, percentEnd)
			);
		}

		return suffix.StartsWith(FailPrefix, StringComparison.Ordinal)
			? new(stepName, journeyName, "fail", null, null)
			: null;
	}

	/// <summary>
	/// Reads the <c>_{count}px</c> tag that follows the percentage in a diff filename.
	/// </summary>
	/// <param name="percentText">The diff suffix with <see cref="DiffPrefix"/> already removed.</param>
	/// <param name="percentEnd">Index of the '%' the tag follows; negative when there was no percentage.</param>
	/// <returns>The count, or <c>null</c> when the filename carries no such tag.</returns>
	private static int? ParsePixelCount(string percentText, int percentEnd)
	{
		if (percentEnd < 0)
		{
			return null;
		}

		var tag = percentText[(percentEnd + 1)..];
		var countEnd = tag.IndexOf(PixelCountSuffix, StringComparison.Ordinal);
		return
			tag.StartsWith(PixelCountPrefix, StringComparison.Ordinal)
			&& countEnd > PixelCountPrefix.Length
			&& int.TryParse(
				tag[PixelCountPrefix.Length..countEnd],
				System.Globalization.CultureInfo.InvariantCulture,
				out var count
			)
			? count
			: null;
	}

	private static string Attributed(TestStep testStep) => Attributed(testStep.StepName, testStep.JourneyName);

	private static string Attributed(string stepName, string journeyName) => $"{stepName} [{journeyName}]";
}
