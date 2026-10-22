using System.Diagnostics;
using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

/// <summary>
/// Covers <see cref="ProcessRunner"/> using only standard Unix programs, so the tests run without
/// Appium, xcrun or adb installed.
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
	public void RunForBytesReturnsBinaryOutputUnaltered()
	{
		// Every byte value, most of which are not valid UTF-8 on their own: any decoding as text on
		// the way through would replace or drop them.
		byte[] expected = [.. Enumerable.Range(0, 256).Select(value => (byte)value)];
		var path = Path.Combine(Path.GetTempPath(), $"process_runner_bytes_{Guid.NewGuid():N}.bin");
		File.WriteAllBytes(path, expected);
		try
		{
			var result = ProcessRunner.RunForBytes("cat", [path], 10);

			_ = result.ExitCode.Should().Be(0);
			_ = result.Output.Should().Equal(expected);
		}
		finally
		{
			File.Delete(path);
		}
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
	public void StartReturnsTheProcessIdOfTheProgram()
	{
		const string Program = "sleep";
		var outputDirectory = Directory.CreateTempSubdirectory(OutputDirectoryPrefix).FullName;
		try
		{
			using var started = Process.GetProcessById(
				ProcessRunner.Start(Program, ["30"], Path.Combine(outputDirectory, "output.log"))
			);
			try
			{
				_ = WaitFor(
						() =>
						{
							started.Refresh();
							return started.ProcessName;
						},
						name => name == Program
					)
					.Should()
					.Be(Program);
			}
			finally
			{
				started.Kill();
			}
		}
		finally
		{
			Directory.Delete(outputDirectory, true);
		}
	}

	[Test]
	public void StartWritesBothOfTheProgramsStreamsToTheFile()
	{
		const string Expected = "out\nerr\n";
		var outputDirectory = Directory.CreateTempSubdirectory(OutputDirectoryPrefix).FullName;
		var outputPath = Path.Combine(outputDirectory, "output.log");
		try
		{
			_ = ProcessRunner.Start("sh", ["-c", "echo out; echo err >&2"], outputPath);

			_ = WaitFor(() => ReadIfExists(outputPath), text => text == Expected).Should().Be(Expected);
		}
		finally
		{
			Directory.Delete(outputDirectory, true);
		}
	}

	[Test]
	public void StartOfAProgramThatDoesNotExistWritesWhyToTheFile()
	{
		const string Program = "/no/such/binary/zzz";
		var outputDirectory = Directory.CreateTempSubdirectory(OutputDirectoryPrefix).FullName;
		var outputPath = Path.Combine(outputDirectory, "output.log");
		try
		{
			_ = ProcessRunner.Start(Program, [], outputPath);

			_ = WaitFor(() => ReadIfExists(outputPath), text => text.Contains(Program, StringComparison.Ordinal))
				.Should()
				.Contain(Program);
		}
		finally
		{
			Directory.Delete(outputDirectory, true);
		}
	}

	[Test]
	public void StartCreatesTheOutputFilesDirectory()
	{
		var outputDirectory = Directory.CreateTempSubdirectory(OutputDirectoryPrefix).FullName;
		var missingDirectory = Path.Combine(outputDirectory, "missing");
		var outputPath = Path.Combine(missingDirectory, "output.log");
		try
		{
			_ = ProcessRunner.Start("true", [], outputPath);

			_ = Directory.Exists(missingDirectory).Should().BeTrue();
		}
		finally
		{
			// Wait for the program to create its file: a file created while the directory is being deleted
			// makes the delete fail.
			_ = WaitFor(() => File.Exists(outputPath) ? outputPath : string.Empty, path => path.Length > 0);
			Directory.Delete(outputDirectory, true);
		}
	}

	[Test]
	public void StartFailsWhenTheOutputFilesDirectoryCannotBeCreated() =>
		_ = FluentActions
			.Invoking(() => ProcessRunner.Start("true", [], "/dev/null/output.log"))
			.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*mkdir*");

	[Test]
	public void RunFireAndForgetDrainsStdoutAndDoesNotHang()
	{
		// 256 KB to stdout in fire-and-forget mode. The discarding readers must drain so
		// the child can complete; the call must return inside the timeout.
		var sw = Stopwatch.StartNew();
		ProcessRunner.Run("dd", ["if=/dev/zero", "bs=1024", "count=256", "status=none"], 10);
		sw.Stop();
		_ = sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
	}

	private const string OutputDirectoryPrefix = "process_runner_start_";

	/// <summary>Reads a value every 50 ms until <paramref name="isDone"/> accepts it or five seconds have passed.</summary>
	/// <param name="read">Reads the current value.</param>
	/// <param name="isDone">Whether a value is the one to stop at.</param>
	/// <returns>The value read last: one <paramref name="isDone"/> accepts unless the time ran out.</returns>
	private static string WaitFor(Func<string> read, Func<string, bool> isDone)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		var value = read();
		while (!isDone(value) && DateTime.UtcNow < deadline)
		{
			Thread.Sleep(50);
			value = read();
		}

		return value;
	}

	/// <summary>Reads a file that may not exist yet.</summary>
	/// <param name="path">The file.</param>
	/// <returns>Its text, or an empty string while it does not exist.</returns>
	private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}
