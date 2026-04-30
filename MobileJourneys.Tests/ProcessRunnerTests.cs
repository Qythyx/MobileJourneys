using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Covers <see cref="ProcessRunner"/>'s argument-list passthrough, concurrent stdout/stderr
/// drain, and timeout behavior. Uses portable shell-builtin commands so the suite works
/// without depending on Appium/xcrun/adb being installed.
/// </summary>
[TestFixture]
public sealed class ProcessRunnerTests
{
	[Test]
	public void RunWithResultDotnetVersionReturnsZeroExitAndVersionString()
	{
		var result = ProcessRunner.RunWithResult("dotnet", ["--version"]);
		_ = result.Should().NotBeNull();
		_ = result.ExitCode.Should().Be(0);
		_ = result.Output.Trim().Should().NotBeEmpty();
		_ = result.Error.Should().BeEmpty();
	}

	[Test]
	public void RunWithResultNonexistentBinaryReturnsErrorResultWithMinusOneExit()
	{
		var result = ProcessRunner.RunWithResult("/no/such/binary/zzz", ["--version"]);
		_ = result.Should().NotBeNull();
		_ = result.ExitCode.Should().Be(-1);
		_ = result.Error.Should().Contain("/no/such/binary/zzz");
	}

	[Test]
	public void RunWithResultLargeStdoutDoesNotDeadlock()
	{
		// 256 KB of zeros via dd — well above the ~64 KB pipe buffer that would deadlock
		// a serial-reader implementation. Test must complete inside the timeout.
		var result = ProcessRunner.RunWithResult("dd", ["if=/dev/zero", "bs=1024", "count=256", "status=none"], 10);
		_ = result.Should().NotBeNull();
		_ = result.ExitCode.Should().Be(0);
		_ = result.Output.Length.Should().Be(256 * 1024);
	}

	[Test]
	public void RunWithResultLargeStderrDoesNotDeadlock()
	{
		// dd writes its progress summary to stderr (omitted via status=none); use sh -c to
		// produce a large stderr output and verify the concurrent reader doesn't block on it.
		var result = ProcessRunner.RunWithResult("sh", ["-c", "dd if=/dev/zero bs=1024 count=256 status=none >&2"], 10);
		_ = result.Should().NotBeNull();
		_ = result.ExitCode.Should().Be(0);
		_ = result.Error.Length.Should().Be(256 * 1024);
	}

	[Test]
	public void RunWithResultArgumentWithSpacesPassesAsSingleArg()
	{
		// echo prints args separated by single spaces. If the args were joined into a shell
		// string and re-parsed, "hello world" would become two args. With ArgumentList it
		// stays as one, and echo prints it verbatim.
		var result = ProcessRunner.RunWithResult("echo", ["hello world"]);
		_ = result.Should().NotBeNull();
		_ = result.ExitCode.Should().Be(0);
		_ = result.Output.Trim().Should().Be("hello world");
	}

	[Test]
	public void RunWithResultTimeoutExceededReturnsTimeoutError()
	{
		// sleep 5 with a 1-second timeout — the runner must kill the child and return.
		var result = ProcessRunner.RunWithResult("sleep", ["5"], 1);
		_ = result.Should().NotBeNull();
		_ = result.ExitCode.Should().Be(-1);
		_ = result.Error.Should().Contain("did not exit within");
	}

	[Test]
	public void RunFireAndForgetDrainsStdoutAndDoesNotHang()
	{
		// 256 KB to stdout in fire-and-forget mode. The discarding readers must drain so
		// the child can complete; the call must return inside the timeout.
		var sw = System.Diagnostics.Stopwatch.StartNew();
		ProcessRunner.Run("dd", ["if=/dev/zero", "bs=1024", "count=256", "status=none"], 10);
		sw.Stop();
		_ = sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
	}
}
