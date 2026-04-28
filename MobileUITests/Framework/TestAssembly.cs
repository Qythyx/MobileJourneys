using System.Reflection;

namespace MobileUITests.Framework;

internal static class TestAssembly
{
	private static readonly Assembly EntryAssembly =
		Assembly.GetEntryAssembly()
		?? throw new InvalidOperationException(
			"GetEntryAssembly returned null. The MobileUITests framework expects to run inside a console "
				+ "test executable; non-console hosting contexts are not supported."
		);

	public static readonly string Name = EntryAssembly.GetName().Name!;

	public static readonly string ProjectRootPath = GetMetadata("ProjectDir");

	public static readonly string RepoRoot = GetMetadata("RepoRoot");

	private static string GetMetadata(string key) =>
		EntryAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;
}
