using System.Reflection;

namespace MobileJourneys.Framework;

/// <summary>
/// Convenience accessors for assembly-level metadata the consumer test executable
/// declares via <c>&lt;AssemblyMetadata&gt;</c> items in its csproj. Resolved against
/// <see cref="Assembly.GetEntryAssembly"/>, which is the test executable when MTP runs
/// the suite.
/// </summary>
public static class TestAssembly
{
	private static readonly Assembly EntryAssembly =
		Assembly.GetEntryAssembly()
		?? throw new InvalidOperationException(
			"GetEntryAssembly returned null. The MobileJourneys framework expects to run inside a console "
				+ "test executable; non-console hosting contexts are not supported."
		);

	public static readonly string Name = EntryAssembly.GetName().Name!;

	public static readonly string ProjectRootPath = GetMetadata("ProjectDir");

	public static readonly string RepoRoot = GetMetadata("RepoRoot");

	private static string GetMetadata(string key) =>
		EntryAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;
}
