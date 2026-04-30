using System.Diagnostics;

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

	internal sealed record ShellResult(
		string Output,
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
	/// Captures stdout/stderr/exit code. Returns <c>null</c> if the process fails to start.
	/// Both streams are drained concurrently so a child writing &gt;64 KB to either pipe
	/// can't deadlock against a serial reader.
	/// </summary>
	public static ShellResult? RunWithResult(string fileName, IReadOnlyList<string> arguments, int timeoutSeconds = 5)
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
				return new ShellResult(
					"",
					$"Process '{Describe(fileName, arguments)}' failed to start",
					-1,
					ShellResultStatus.FailedToStart
				);
			}

			var stdoutTask = process.StandardOutput.ReadToEndAsync();
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

				return new ShellResult(
					"",
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

			return new ShellResult(stdoutTask.Result, stderrTask.Result, process.ExitCode);
		}
		catch (Exception ex)
		{
			return new ShellResult(
				"",
				$"Exception running '{Describe(fileName, arguments)}': {ex.Message}",
				-1,
				ShellResultStatus.FailedToStart
			);
		}
	}

	private static string Describe(string fileName, IReadOnlyList<string> arguments) =>
		arguments.Count == 0 ? fileName : $"{fileName} {string.Join(" ", arguments)}";
}
