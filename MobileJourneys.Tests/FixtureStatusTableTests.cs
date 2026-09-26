using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class FixtureStatusTableTests
{
	private static readonly IosPlatformConfig Single = new(
		"26.2",
		"iPhone 17 Pro",
		IsLightTheme: true,
		"com.example.app",
		"/path/to/app.app",
		100,
		210,
		3 * 2,
		0.005
	);

	private static readonly IosPlatformConfig Paired = Single with { DeviceName = "iPhone 16e", Instances = 2 };

	[Test]
	public void SingleInstanceCellIsTheBareText()
	{
		var table = new FixtureStatusTable([(Single, 3)]);

		table.Step(Single, 1, "Login", 2, 5, "tap sign in");

		_ = table.CurrentCell(Single).Should().Be("Login 2/5 tap sign in");
	}

	[Test]
	public void AReadyWorkerSaysWhatItIsDoingRatherThanNothing()
	{
		var table = new FixtureStatusTable([(Single, 3)]);

		table.Ready(Single, 1);

		_ = table.CurrentCell(Single).Should().Be("device up — preparing it…");
	}

	[Test]
	public void APreparingWorkerNamesTheJourneyItIsSettingUp()
	{
		var table = new FixtureStatusTable([(Single, 3)]);

		table.Step(Single, 1, "Login", 5, 5, "tap sign in");
		table.Preparing(Single, 1, "Checkout");

		_ = table.CurrentCell(Single).Should().Be("Checkout — launching the app…");
	}

	[Test]
	public void MultiInstanceCellHasOneNumberedLinePerWorker()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		table.Step(Paired, 2, "Checkout", 1, 4, "open cart");

		_ = table.CurrentCell(Paired).Should().Be("1: starting the device…\n2: Checkout 1/4 open cart");
	}

	[Test]
	public void LostWorkerLineStaysWhileOthersAdvance()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		table.Lost(Paired, 2, "its session died and would not reopen.");
		table.Step(Paired, 1, "Login", 3, 5, "enter password");

		_ = table
			.CurrentCell(Paired)
			.Should()
			.Be("1: Login 3/5 enter password\n2: lost — its session died and would not reopen.");
	}

	[Test]
	public void FinishedFixtureCollapsesToOneLine()
	{
		var table = new FixtureStatusTable([(Paired, 1)]);

		table.Step(Paired, 1, "Login", 1, 1, "done");
		table.JourneyDone(Paired, true);

		_ = table.CurrentCell(Paired).Should().Be("all journeys completed");
	}
}
