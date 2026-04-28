using Microsoft.Testing.Platform.Extensions.Messages;

namespace MobileUITests.Framework;

/// <summary>
/// Builds an MTP <see cref="TestNode"/> for a <see cref="TestCase"/> with the identity
/// metadata that IDE test explorers (e.g., C# Dev Kit) use to group tests into a readable
/// namespace/class/method tree.
/// </summary>
internal static class TestNodeFactory
{
	public static TestNode Create(TestCase testCase, string testNodeNamespace)
	{
		var node = new TestNode { Uid = testCase.Uid, DisplayName = testCase.DisplayName };
		node.Properties.Add(
			new TestMethodIdentifierProperty(
				TestAssembly.Name,
				testNodeNamespace,
				testCase.Journey.Name,
				testCase.Config.DisplayName,
				0,
				[],
				"System.Void"
			)
		);
		return node;
	}
}
