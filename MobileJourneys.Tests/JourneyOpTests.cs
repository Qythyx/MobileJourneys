using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class JourneyOpTests
{
	private sealed record TestOp(string? Target = null) : JourneyOp(Target);

	private sealed record OverridingOp(string? Target = null) : JourneyOp(Target)
	{
		protected override string Name => "ParentName";
	}

	[Test]
	public void Label_WithNullTarget_ReturnsName() => new TestOp().Label.Should().Be("TestOp");

	[Test]
	public void Label_WithSimpleTarget_AppendsTargetToName() =>
		new TestOp("HamburgerMenu").Label.Should().Be("TestOp HamburgerMenu");

	[Test]
	public void Label_WithUnderscoredTarget_KeepsOnlyLastSegment() =>
		new TestOp("TitleView_HamburgerMenu").Label.Should().Be("TestOp HamburgerMenu");

	[Test]
	public void Label_WithMultipleUnderscores_KeepsOnlyTheLastSegment() =>
		new TestOp("Foo_Bar_Baz").Label.Should().Be("TestOp Baz");

	[Test]
	public void Label_WithFilenameInvalidChars_ReplacesWithUnderscores()
	{
		// Target contains slash and colon which are invalid on most filesystems.
		var op = new TestOp("Login / Create Account");
		op.Label.Should().NotContain("/");
		op.Label.Should().Be("TestOp Login _ Create Account");
	}

	[Test]
	public void Label_WhenNameIsOverridden_UsesOverride() =>
		new OverridingOp("Target").Label.Should().Be("ParentName Target");

	[Test]
	public void Label_WhenNameIsOverriddenAndTargetIsNull_ReturnsOverride() =>
		new OverridingOp().Label.Should().Be("ParentName");
}
