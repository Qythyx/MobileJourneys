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

	/// <summary>The simple name of the consumer's test executable assembly.</summary>
	public static readonly string Name = EntryAssembly.GetName().Name!;

	/// <summary>
	/// The directory containing the consumer's csproj. Read from the
	/// <c>&lt;AssemblyMetadata Include="ProjectDir" Value="$(MSBuildProjectDirectory)" /&gt;</c>
	/// item the consumer's csproj must declare.
	/// </summary>
	public static readonly string ProjectRootPath = GetMetadata("ProjectDir");

	/// <summary>
	/// The repository root the consumer is checked out into. Read from the
	/// <c>&lt;AssemblyMetadata Include="RepoRoot" Value="$(RepoRoot)" /&gt;</c> item the
	/// consumer's csproj must declare.
	/// </summary>
	public static readonly string RepoRoot = GetMetadata("RepoRoot");

	/// <summary>
	/// The MobileJourneys framework assembly's informational version (or assembly version
	/// if no informational version is set). Used for MTP framework/provider Version slots.
	/// </summary>
	public static readonly string FrameworkVersion =
		typeof(TestAssembly).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? typeof(TestAssembly).Assembly.GetName().Version?.ToString()
		?? "0.0.0";

	private static string GetMetadata(string key) =>
		EntryAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;
}
