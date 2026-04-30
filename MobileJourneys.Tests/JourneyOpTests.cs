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
	public void LabelWithNullTargetReturnsName() => new TestOp().Label.Should().Be("TestOp");

	[Test]
	public void LabelWithSimpleTargetAppendsTargetToName() =>
		new TestOp("HamburgerMenu").Label.Should().Be("TestOp HamburgerMenu");

	[Test]
	public void LabelWithUnderscoredTargetKeepsOnlyLastSegment() =>
		new TestOp("TitleView_HamburgerMenu").Label.Should().Be("TestOp HamburgerMenu");

	[Test]
	public void LabelWithMultipleUnderscoresKeepsOnlyTheLastSegment() =>
		new TestOp("Foo_Bar_Baz").Label.Should().Be("TestOp Baz");

	[Test]
	public void LabelWithFilenameInvalidCharsReplacesWithUnderscores()
	{
		// Target contains slash and colon which are invalid on most filesystems.
		var op = new TestOp("Login / Create Account");
		_ = op.Label.Should().NotContain("/");
		_ = op.Label.Should().Be("TestOp Login _ Create Account");
	}

	[Test]
	public void LabelWhenNameIsOverriddenUsesOverride() =>
		new OverridingOp("Target").Label.Should().Be("ParentName Target");

	[Test]
	public void LabelWhenNameIsOverriddenAndTargetIsNullReturnsOverride() =>
		new OverridingOp().Label.Should().Be("ParentName");
}
