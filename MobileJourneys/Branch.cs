namespace MobileJourneys;

/// <summary>
/// A node in a <see cref="JourneyTree"/>: a named chain of steps, optionally followed by the child
/// nodes that diverge after them. A node <em>with</em> children is an interior node — a prefix
/// shared by every journey beneath it. A <em>childless</em> node is terminal: it becomes a
/// <see cref="JourneyDefinition"/> named after it, whose full step sequence is the concatenation of
/// every node's steps along the path from the tree root down to it. The name is the folder its
/// screenshots live in, nested under its parent's folder.
/// </summary>
/// <param name="name">Folder name for this node's screenshots; for a terminal node, also the journey's name.</param>
/// <param name="steps">The steps this node contributes to every journey passing through it.</param>
/// <param name="children">The nodes that diverge after this node's steps; leave empty for a terminal (leaf) node.</param>
public sealed class Branch(string name, JourneyStep[] steps, Branch[] children)
{
	/// <summary>Folder name for this node's screenshots, nested under the parent node's folder.</summary>
	public string Name { get; } =
		name.Length > 0 ? name : throw new ArgumentException("Must not be empty.", nameof(name));

	/// <summary>The steps this node contributes to every journey passing through it.</summary>
	public JourneyStep[] Steps { get; } = steps;

	/// <summary>The nodes that diverge after this node's steps; empty for a terminal (leaf) node.</summary>
	public Branch[] Children { get; } = Validate(name, steps, children);

	/// <summary>Validates that sibling nodes have distinct names and returns them.</summary>
	/// <param name="children">The sibling nodes to validate.</param>
	/// <param name="ownerName">Name of the owning node, used in the error message.</param>
	internal static Branch[] EnsureDistinctNames(Branch[] children, string ownerName)
	{
		var duplicates = children.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
		return duplicates.Count == 0
			? children
			: throw new ArgumentException(
				$"Node '{ownerName}' has children with duplicate names: {string.Join(", ", duplicates)}.",
				nameof(children)
			);
	}

	private static Branch[] Validate(string name, JourneyStep[] steps, Branch[] children)
	{
		if (steps.Length == 0 && children.Length == 0)
		{
			// A childless node with no steps re-runs the path its siblings already cover, asserts
			// nothing they do not, and owns no screenshot; a node with neither does nothing at all.
			throw new ArgumentException(
				$"Node '{name}' has neither steps nor children, so it runs nothing and owns no screenshot. "
					+ "Give it steps to make it a journey, or children to make it a shared prefix.",
				nameof(steps)
			);
		}

		return EnsureDistinctNames(children, name);
	}
}
