using AwesomeAssertions;
using MobileJourneys.Framework;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class RunOptionsTests
{
	[Test]
	public void NoArgumentsAsksTheReader()
	{
		var options = RunOptions.Parse([]);

		_ = options.Mode.Should().Be(RunMode.Interactive);
		_ = options.Error.Should().BeNull();
	}

	[Test]
	public void BlankArgumentsCountAsNone()
	{
		var options = RunOptions.Parse(["", "  "]);

		_ = options.Mode.Should().Be(RunMode.Interactive);
	}

	[Test]
	public void RunFlagStartsARun()
	{
		var options = RunOptions.Parse(["--run"]);

		_ = options.Mode.Should().Be(RunMode.Run);
		_ = options.Filters.Should().BeEmpty();
		_ = options.JourneyNames.Should().BeEmpty();
		_ = options.Rerun.Should().BeFalse();
	}

	[Test]
	public void FilterWithoutRunIsRejected()
	{
		var options = RunOptions.Parse(["--filter", "About"]);

		_ = options.Mode.Should().Be(RunMode.Help);
		_ = options.Error.Should().Contain("--run");
	}

	[Test]
	public void RerunWithoutRunIsRejected()
	{
		var options = RunOptions.Parse(["--rerun"]);

		_ = options.Mode.Should().Be(RunMode.Help);
		_ = options.Error.Should().Contain("--run");
	}

	[Test]
	public void FiltersAccumulateAcrossRepeatedAndMultiValuedFlags()
	{
		var options = RunOptions.Parse(["--run", "--filter", "iPhone 17 Pro", "About", "--filter", "light"]);

		_ = options.Mode.Should().Be(RunMode.Run);
		_ = options.Filters.Should().Equal("iPhone 17 Pro", "About", "light");
	}

	[Test]
	public void RerunNarrowsARun()
	{
		var options = RunOptions.Parse(["--run", "--rerun"]);

		_ = options.Mode.Should().Be(RunMode.Run);
		_ = options.Rerun.Should().BeTrue();
	}

	[Test]
	public void FilterWithoutAValueIsRejected()
	{
		var options = RunOptions.Parse(["--run", "--filter"]);

		_ = options.Mode.Should().Be(RunMode.Help);
		_ = options.Error.Should().Contain("--filter");
	}

	[Test]
	public void JourneyWithoutRunIsRejected()
	{
		var options = RunOptions.Parse(["--journey", "Settings"]);

		_ = options.Mode.Should().Be(RunMode.Help);
		_ = options.Error.Should().Contain("--run");
	}

	[Test]
	public void JourneyNamesAccumulateAcrossRepeatedAndMultiValuedFlags()
	{
		var options = RunOptions.Parse(["--run", "--journey", "Settings", "About", "--journey", "Loading"]);

		_ = options.Mode.Should().Be(RunMode.Run);
		_ = options.JourneyNames.Should().Equal("Settings", "About", "Loading");
	}

	[Test]
	public void JourneyWithoutAValueIsRejected()
	{
		var options = RunOptions.Parse(["--run", "--journey"]);

		_ = options.Mode.Should().Be(RunMode.Help);
		_ = options.Error.Should().Contain("--journey");
	}

	[Test]
	public void JourneyAndFilterAreIndependent()
	{
		var options = RunOptions.Parse(["--run", "--journey", "Settings", "--filter", "iPhone 17 Pro"]);

		_ = options.JourneyNames.Should().Equal("Settings");
		_ = options.Filters.Should().Equal("iPhone 17 Pro");
	}

	[Test]
	public void UsageDocumentsTheJourneyFlag()
	{
		var usage = RunOptions.Usage("My Suite");

		_ = usage.Should().Contain("--journey");
	}

	[Test]
	public void UnrecognizedArgumentIsRejected()
	{
		var options = RunOptions.Parse(["--nope"]);

		_ = options.Mode.Should().Be(RunMode.Help);
		_ = options.Error.Should().Contain("--nope");
	}

	[TestCase("--list-extraneous", RunMode.ListExtraneous)]
	[TestCase("--delete-extraneous", RunMode.DeleteExtraneous)]
	[TestCase("--review", RunMode.Review)]
	[TestCase("--help", RunMode.Help)]
	[TestCase("-h", RunMode.Help)]
	public void ModeFlagsNeedNoRunFlag(string flag, RunMode expected)
	{
		var options = RunOptions.Parse([flag]);

		_ = options.Mode.Should().Be(expected);
		_ = options.Error.Should().BeNull();
	}

	[Test]
	public void UsageNamesTheSuiteAndTheRunFlag()
	{
		var usage = RunOptions.Usage("My Suite");

		_ = usage.Should().Contain("My Suite").And.Contain("--run");
	}
}
