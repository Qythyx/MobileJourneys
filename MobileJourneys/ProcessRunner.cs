using System.Diagnostics;
using System.Globalization;

namespace MobileJourneys;

/// <summary>
/// Internal wrapper around <see cref="Process"/> used by platform configs to invoke
/// out-of-process tools (<c>xcrun</c>, <c>adb</c>) without duplicating the boilerplate.
/// Arguments are passed individually and forwarded via <see cref="ProcessStartInfo.ArgumentList"/>
/// so callers don't have to worry about shell quoting / escaping.
/// </summary>
internal static class ProcessRunner
{
	internal enum ShellResultStatus
	{
		Completed,
		FailedToStart,
		TimedOut,
	}

	internal sealed record ShellResult<TOutput>(
		TOutput Output,
		string Error,
		int ExitCode,
		ShellResultStatus Status = ShellResultStatus.Completed
	);

	/// <summary>
	/// Fire-and-forget. Waits up to <paramref name="timeoutSeconds"/>; output is drained
	/// in the background and discarded so the child can never block on a full pipe buffer.
	/// </summary>
	public static void Run(string fileName, IReadOnlyList<string> arguments, int timeoutSeconds = 10)
	{
		var psi = new ProcessStartInfo
		{
			FileName = fileName,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		foreach (var arg in arguments)
		{
			psi.ArgumentList.Add(arg);
		}

		using var process = new Process { StartInfo = psi };
		process.OutputDataReceived += static (_, _) => { };
		process.ErrorDataReceived += static (_, _) => { };
		if (!process.Start())
		{
			return;
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		_ = process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds));
	}

	/// <summary>
	/// Starts a program in the background, where it may outlive this call, and returns without waiting
	/// for it to do anything.
	/// </summary>
	/// <remarks>
	/// The child is put in a process group of its own, through a shell with job control on, so
	/// that a Ctrl+C or a closed terminal aimed at this process does not reach it: the terminal
	/// signals the whole foreground group, and a child left in it dies with the run that started
	/// it. Ignoring the signals instead does not hold, because the emulator installs handlers of
	/// its own. The shell is bash because zsh cannot turn job control on without a terminal.
	/// <para/>
	/// Its output goes to a file rather than to a pipe, because a pipe would have to be drained for
	/// as long as the child lives — and once this process stopped reading, the child would take a
	/// SIGPIPE on its next write. Inheriting the console instead is no good either: the caller's
	/// display owns it.
	/// </remarks>
	/// <param name="fileName">The program to run.</param>
	/// <param name="arguments">Its arguments, passed through without shell interpretation.</param>
	/// <param name="outputPath">
	/// The file its standard output and error are written to, replaced if it exists. Its directory is
	/// created when missing.
	/// </param>
	/// <returns>
	/// The program's process id. The process may already have exited, as when the program does not
	/// exist; the reason is then in the output file, unless the file itself could not be opened.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The program could not be started in the background, as when the output file's directory cannot
	/// be created.
	/// </exception>
	public static int Start(string fileName, IReadOnlyList<string> arguments, string outputPath)
	{
		var result = RunWithResult(
			"/bin/bash",
			[
				"-c",
				"set -m; output=$1; shift; mkdir -p \"$(dirname \"$output\")\" || exit; \"$@\" </dev/null >\"$output\" 2>&1 & echo $!",
				"bash",
				outputPath,
				fileName,
				.. arguments,
			]
		);
		return
			result is { ExitCode: 0 }
			&& int.TryParse(result.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
			? pid
			: throw new InvalidOperationException(
				$"'{Describe(fileName, arguments)}' could not be started in the background with its output in {outputPath}: "
					+ (
						result?.Error.Trim() is { Length: > 0 } error
							? error
							: $"the shell printed '{result?.Output.Trim()}'"
					)
			);
	}

	/// <summary>
	/// Captures stdout as text, with stderr and the exit code.
	/// </summary>
	public static ShellResult<string>? RunWithResult(
		string fileName,
		IReadOnlyList<string> arguments,
		int timeoutSeconds = 5
	) => Capture(fileName, arguments, timeoutSeconds, static stdout => stdout.ReadToEndAsync(), string.Empty);

	/// <summary>
	/// Captures stdout as bytes, with stderr and the exit code, for a tool whose output is binary.
	/// </summary>
	/// <param name="fileName">The program to run.</param>
	/// <param name="arguments">Its arguments, passed through without shell interpretation.</param>
	/// <param name="timeoutSeconds">How long it may run before it is killed.</param>
	/// <returns>What it wrote and how it ended; the output is empty unless it completed.</returns>
	public static ShellResult<byte[]> RunForBytes(
		string fileName,
		IReadOnlyList<string> arguments,
		int timeoutSeconds
	) =>
		Capture(
			fileName,
			arguments,
			timeoutSeconds,
			static async stdout =>
			{
				using var buffer = new MemoryStream();
				await stdout.BaseStream.CopyToAsync(buffer).ConfigureAwait(false);
				return buffer.ToArray();
			},
			[]
		);

	/// <summary>
	/// Runs a process to completion, capturing stdout with the given reader, and stderr and the exit
	/// code with it. Both streams are drained concurrently so a child writing &gt;64 KB to either
	/// pipe can't deadlock against a serial reader.
	/// </summary>
	/// <param name="fileName">The program to run.</param>
	/// <param name="arguments">Its arguments, passed through without shell interpretation.</param>
	/// <param name="timeoutSeconds">How long it may run before it is killed.</param>
	/// <param name="readOutput">Drains stdout into the form the caller wants.</param>
	/// <param name="noOutput">The output reported when the process did not complete.</param>
	/// <returns>What it wrote and how it ended.</returns>
	private static ShellResult<TOutput> Capture<TOutput>(
		string fileName,
		IReadOnlyList<string> arguments,
		int timeoutSeconds,
		Func<StreamReader, Task<TOutput>> readOutput,
		TOutput noOutput
	)
	{
		var psi = new ProcessStartInfo
		{
			FileName = fileName,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach (var arg in arguments)
		{
			psi.ArgumentList.Add(arg);
		}

		try
		{
			using var process = new Process { StartInfo = psi };
			if (!process.Start())
			{
				return new ShellResult<TOutput>(
					noOutput,
					$"Process '{Describe(fileName, arguments)}' failed to start",
					-1,
					ShellResultStatus.FailedToStart
				);
			}

			var stdoutTask = readOutput(process.StandardOutput);
			var stderrTask = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(timeoutSeconds * 1000))
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
					// Process may have exited between the timeout check and the kill.
				}

				// Observe the drain tasks before the using-block disposes the process. After
				// Kill() the streams close and the readers complete (or fault on
				// ObjectDisposedException once the Process is disposed); either way we don't
				// want unobserved task exceptions leaking out.
				try
				{
					_ = Task.WhenAll(stdoutTask, stderrTask).Wait(TimeSpan.FromSeconds(1));
				}
				catch
				{
					// Drain tasks may fault after Kill — discard.
				}

				return new ShellResult<TOutput>(
					noOutput,
					$"Process '{Describe(fileName, arguments)}' did not exit within {timeoutSeconds}s",
					-1,
					ShellResultStatus.TimedOut
				);
			}

			// WaitForExit(int ms) returns when the OS reports the process has exited but
			// does NOT wait for the redirected stdout/stderr async readers to drain. The
			// parameterless WaitForExit() flushes those streams, so reading the task
			// results below cannot race against an in-flight pipe drain.
			process.WaitForExit();

			return new ShellResult<TOutput>(stdoutTask.Result, stderrTask.Result, process.ExitCode);
		}
		catch (Exception ex)
		{
			return new ShellResult<TOutput>(
				noOutput,
				$"Exception running '{Describe(fileName, arguments)}': {ex.Message}",
				-1,
				ShellResultStatus.FailedToStart
			);
		}
	}

	private static string Describe(string fileName, IReadOnlyList<string> arguments) =>
		arguments.Count == 0 ? fileName : $"{fileName} {string.Join(" ", arguments)}";
}
