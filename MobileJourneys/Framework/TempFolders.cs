using System.Globalization;

namespace MobileJourneys.Framework;

/// <summary>
/// Names the folders under the system's temporary directory that runs write to, and the stamp that
/// orders what is in them.
/// </summary>
internal static class TempFolders
{
	private static readonly string Root = Path.Combine(Path.GetTempPath(), "MobileJourneys");

	/// <summary>The folder each run's own folder is made in.</summary>
	public static string Runs { get; } = Path.Combine(Root, "runs");

	/// <summary>The folder emulators write their output to, which outlives the run that started them.</summary>
	public static string Emulators { get; } = Path.Combine(Root, "emulators");

	/// <summary>Stamps a name with a moment, so that names sort in the order they were made.</summary>
	/// <param name="moment">The local time to stamp.</param>
	/// <returns>The stamp, e.g. <c>20261022-143012</c>.</returns>
	public static string Stamp(DateTime moment) => moment.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}
