using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

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

		_ = table.CurrentCell(TestFixtures.Ios).Should().Be("Login 2/5 tap sign in");
	}

	[Test]
	public void AReadyWorkerSaysWhatItIsDoingRatherThanNothing()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Ready(TestFixtures.Ios, 1);

		_ = table.CurrentCell(TestFixtures.Ios).Should().Be("device up — preparing it…");
	}

	[Test]
	public void APreparingWorkerNamesTheJourneyItIsSettingUp()
	{
		var table = new FixtureStatusTable([(TestFixtures.Ios, 3)]);

		table.Step(TestFixtures.Ios, 1, "Login", 5, 5, "tap sign in");
		table.Preparing(TestFixtures.Ios, 1, "Checkout");

		_ = table.CurrentCell(TestFixtures.Ios).Should().Be("Checkout — launching the app…");
	}

	[Test]
	public void MultiInstanceCellHasOneNumberedLinePerWorker()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		table.Step(Paired, 2, "Checkout", 1, 4, "open cart");

		_ = table.CurrentCell(Paired).Should().Be("1: starting the device…\n2: Checkout 1/4 open cart");
	}

	[Test]
	public void ADeviceStartedAgainShowsOnEveryWorker()
	{
		var table = new FixtureStatusTable([(Paired, 3)]);

		table.StartRetrying(Paired, "an instance exited while booting");

		_ = table
			.CurrentCell(Paired)
			.Should()
			.Be("1: retrying — an instance exited while booting\n2: retrying — an instance exited while booting");
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
