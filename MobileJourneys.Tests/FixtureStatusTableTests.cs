using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;
using Spectre.Console;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class FixtureStatusTableTests
{
	private static readonly IosPlatformConfig Paired = TestFixtures.Ios with
	{
		DeviceName = "iPhone 16e",
		Instances = 2,
	};

	[Test]
	public void SingleInstanceCellIsTheBareText()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Step(TestFixtures.Ios, 1, "Login", 2, 5, "tap sign in");

		_ = Markup.Remove(table.CurrentCell(TestFixtures.Ios)).Should().Be("Login 2/5 tap sign in");
	}

	[Test]
	public void AReadyWorkerSaysWhatItIsDoingRatherThanNothing()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Ready(TestFixtures.Ios, 1);

		_ = Markup.Remove(table.CurrentCell(TestFixtures.Ios)).Should().Be("device up — preparing it…");
	}

	[Test]
	public void APreparingWorkerNamesTheJourneyItIsSettingUp()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Step(TestFixtures.Ios, 1, "Login", 5, 5, "tap sign in");
		table.Preparing(TestFixtures.Ios, 1, "Checkout");

		_ = Markup.Remove(table.CurrentCell(TestFixtures.Ios)).Should().Be("Checkout — launching the app…");
	}

	[Test]
	public void MultiInstanceCellHasOneNumberedLinePerWorker()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		table.Step(Paired, 2, "Checkout", 1, 4, "open cart");

		_ = Markup
			.Remove(table.CurrentCell(Paired))
			.Should()
			.Be("1: starting the device…\n2: Checkout 1/4 open cart");
	}

	[Test]
	public void ADeviceStartedAgainShowsOnEveryWorker()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		table.StartRetrying(Paired, "an instance exited while booting");

		_ = Markup
			.Remove(table.CurrentCell(Paired))
			.Should()
			.Be("1: retrying — an instance exited while booting\n2: retrying — an instance exited while booting");
	}

	[Test]
	public void LostWorkerLineStaysWhileOthersAdvance()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		table.Lost(Paired, 2, "its session died and would not reopen.");
		table.Step(Paired, 1, "Login", 3, 5, "enter password");

		_ = Markup
			.Remove(table.CurrentCell(Paired))
			.Should()
			.Be("1: Login 3/5 enter password\n2: lost — its session died and would not reopen.");
	}

	[Test]
	public void FinishedFixtureCollapsesToOneLine()
	{
		var table = new FixtureStatusTable([(Paired, 1)]);

		table.Step(Paired, 1, "Login", 1, 1, "done");
		table.JourneyDone(Paired, true);

		_ = Markup.Remove(table.CurrentCell(Paired)).Should().Be("all journeys completed");
	}

	[Test]
	public void AStepShowsItsJourneyAndStepNamesInBlueAndItsPositionDim()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Step(TestFixtures.Ios, 1, "Login", 2, 5, "tap sign in");

		_ = table.CurrentCell(TestFixtures.Ios).Should().Be("[blue]Login[/] [dim]2/5[/] [blue]tap sign in[/]");
	}

	[Test]
	public void AWorkerNumberIsDim()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		_ = table.CurrentCell(Paired).Should().StartWith("[dim]1:[/] ");
	}

	[Test]
	public void ARetryingWorkerIsYellow()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Retrying(TestFixtures.Ios, 1, "the session died");

		_ = table.CurrentCell(TestFixtures.Ios).Should().Be("[yellow]retrying — the session died[/]");
	}

	[Test]
	public void AFinishedFixtureIsGreen()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 1)]);

		table.JourneyDone(TestFixtures.Ios, true);

		_ = table.CurrentCell(TestFixtures.Ios).Should().Be("[green]all journeys completed[/]");
	}

	[Test]
	public void AReasonWithBracketsIsShownAsWritten()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Abandoned(TestFixtures.Ios, "adb exited [code 1]");

		_ = Markup.Remove(table.CurrentCell(TestFixtures.Ios)).Should().Be("adb exited [code 1]");
	}
}
