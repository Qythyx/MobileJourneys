using AwesomeAssertions;
using MobileJourneys.Actions;
using MobileJourneys.Expectations;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class JourneyStepTests
{
	[Test]
	public void NameWithNonNoneActionReturnsActionLabel() =>
		new JourneyStep(new Tap("HamburgerMenu")).Name.Should().Be("Tap HamburgerMenu");

	[Test]
	public void NameWithNonNoneActionAndExpectationsStillReturnsActionLabel() =>
		new JourneyStep(new Tap("HamburgerMenu"), [new Found("Drawer")]).Name.Should().Be("Tap HamburgerMenu");

	[Test]
	public void NameWithNoneActionAndOneExpectationReturnsExpectationLabel() =>
		new JourneyStep(new None(), [new Found("Drawer")]).Name.Should().Be("Found Drawer");

	[Test]
	public void NameWithNoneActionAndMultipleExpectationsReturnsFirstExpectationLabel() =>
		new JourneyStep(new None(), [new Found("Drawer"), new NotFound("Spinner")]).Name.Should().Be("Found Drawer");

	[Test]
	public void NameWithNoneActionAndNullExpectReturnsActionLabel() =>
		new JourneyStep(new None()).Name.Should().Be("None");

	[Test]
	public void NameWithNoneActionAndEmptyExpectReturnsActionLabel() =>
		new JourneyStep(new None(), []).Name.Should().Be("None");
}
