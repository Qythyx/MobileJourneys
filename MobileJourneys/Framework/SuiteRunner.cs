using System.Collections.Concurrent;
using MobileJourneys.Viewer;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Service;
using OpenQA.Selenium.Appium.Service.Options;
using Spectre.Console;

namespace MobileJourneys.Framework;

/// <summary>
/// Runs a suite from a plain console entry point. The consumer's <c>Program.Main</c> builds a
/// <see cref="FrameworkConfig"/> and hands it here; everything else — argument parsing, journey
/// selection, the Appium server, the fixture fan-out, and the exit code — is owned by this class.
/// </summary>
public static class SuiteRunner
{
	private const string AppiumHostAddress = "127.0.0.1";

	private const int AppiumHostPort = 4723;

	/// <summary>What an Android crash log says when the APK was built without embedded assemblies.</summary>
	private const string MissingAssembliesMarker = "No assemblies found";

	/// <summary>
	/// How many times to try opening a fixture's Appium session before abandoning it. A further
	/// attempt costs the wait below, and at most one of them a device restart besides; abandoning
	/// the fixture costs every journey selected for it.
	/// </summary>
	private const int SessionStartAttempts = 3;

	/// <summary>
	/// How many sessions a fixture may lose mid-run before it is abandoned. Sessions die
	/// independently of each other, so a fixture needing several is one whose device is unwell, and
	/// carrying on there reports the device's condition as failing journeys.
	/// </summary>
	private const int SessionRecoveryAttempts = 3;

	/// <summary>
	/// How many journeys a worker may run again because the system froze the app under them. One is
	/// the device having a bad moment; a device that keeps doing it is telling us about the machine
	/// it runs on, and every journey it takes after that says the same thing.
	/// </summary>
	private const int NotRespondingRetries = 3;

	/// <summary>How many lines of the Appium server's output to keep back for a post-mortem.</summary>
	private const int AppiumOutputTailLines = 200;

	/// <summary>
	/// How long to give a device to become usable before retrying its session. The wait returns as
	/// soon as the device is ready, so only a fixture already in trouble pays for a generous one.
	/// </summary>
	private static readonly TimeSpan DeviceReadyTimeout = TimeSpan.FromMinutes(3);

	/// <summary>Runs whatever the command line asked for.</summary>
	/// <param name="config">The suite's journeys, fixtures, and storage.</param>
	/// <param name="args">The arguments as passed to <c>Main</c>.</param>
	/// <returns>The process exit code.</returns>
	public static async Task<int> RunAsync(FrameworkConfig config, string[] args)
	{
		var options = RunOptions.Parse(args);
		if (options.Mode == RunMode.Interactive)
		{
			if (!IsInteractive)
			{
				Console.Error.WriteLine("Nothing to do, and no terminal to offer the menu in.");
				Console.Error.WriteLine();
				Console.Error.WriteLine(RunOptions.Usage(config.DisplayName));
				return 2;
			}

			options = InteractiveMenu.Choose(config);
		}

		switch (options.Mode)
		{
			case RunMode.Help:
				if (options.Error is { } error)
				{
					Console.Error.WriteLine(error);
					Console.Error.WriteLine();
					Console.Error.WriteLine(RunOptions.Usage(config.DisplayName));
					return 2;
				}
				Console.WriteLine(RunOptions.Usage(config.DisplayName));
				return 0;

			case RunMode.Quit:
				return 0;

			case RunMode.ListExtraneous:
				return ReportExtraneous(config, delete: false);

			case RunMode.DeleteExtraneous:
				return ReportExtraneous(config, delete: true);

			case RunMode.Review:
				return ScreenshotViewer.RunReviewServer(config);

			default:
				var exitCode = await RunJourneysAsync(config, options).ConfigureAwait(false);
				// Refresh the static viewer page so it reflects this run's baselines and artifacts.
				ScreenshotViewer.WriteStaticAssets(config);
				return exitCode;
		}
	}

	/// <summary>
	/// Lists — or deletes — screenshots no current journey references.
	/// </summary>
	/// <param name="config">The suite's journeys and storage.</param>
	/// <param name="delete">Whether to delete what is found rather than only listing it.</param>
	/// <returns>0 when nothing is extraneous or the extras were deleted; 1 when listing found some,
	/// so CI can gate on the check.</returns>
	private static int ReportExtraneous(FrameworkConfig config, bool delete)
	{
		var extraneous = config.FindExtraneous(delete);
		if (extraneous.Count == 0)
		{
			Console.WriteLine(delete ? "No extraneous screenshots to delete." : "No extraneous screenshots found.");
			return 0;
		}

		Console.WriteLine(
			delete
				? $"Deleted {extraneous.Count} extraneous screenshot file(s)/folder(s):"
				: $"Found {extraneous.Count} extraneous screenshot file(s)/folder(s):"
		);
		foreach (var path in extraneous)
		{
			Console.WriteLine($"  {path}");
		}

		return delete ? 0 : 1;
	}

	private static async Task<int> RunJourneysAsync(FrameworkConfig config, RunOptions options)
	{
		// Fail fast on missing external deps (Appium, xcrun, adb) with an install hint instead of
		// producing cryptic Appium errors mid-session.
		DependencyChecker.Verify(config);

		var manager = new ScreenshotManager(config.Storage);
		var all =
			(IReadOnlyList<TestCase>)
				[.. config.PlatformConfigs.SelectMany(c => config.Journeys.Select(j => new TestCase(c, j)))];
		var selected = all.Where(tc =>
				(
					options.JourneyNames.Count == 0
					|| options.JourneyNames.Any(name =>
						string.Equals(name, tc.Journey.Name, StringComparison.OrdinalIgnoreCase)
					)
				)
				&& (
					options.Filters.Count == 0
					|| options.Filters.All(f => tc.Uid.Contains(f, StringComparison.OrdinalIgnoreCase))
				)
				&& (!options.Rerun || manager.HasFailureArtifacts(tc.Config, tc.Journey))
			)
			.ToList();

		RunReporter.Header(config.DisplayName, selected.Count, all.Count);
		if (selected.Count == 0)
		{
			RunReporter.Note("No journeys matched the filters.");
			return 0;
		}

		RunReporter reporter =
			options.ReportTo is { } reportUrl ? new WebReporter(reportUrl, selected)
			: IsInteractive ? new LiveStatusReporter(selected)
			: new ConsoleReporter();

		// Before the server starts, so the sweep cannot reach the helpers this run is about to spawn.
		foreach (var platformConfig in config.PlatformConfigs.DistinctBy(p => p.Platform))
		{
			platformConfig.KillStaleHelperProcesses();
		}

		using var appiumService = new AppiumServiceBuilder()
			.WithIPAddress(AppiumHostAddress)
			.UsingPort(AppiumHostPort)
			.WithArguments(new OptionCollector().AddArguments(new("--allow-insecure", "*:adb_shell")))
			.Build();

		// The runtime's own kill would orphan the Appium server, so Ctrl+C is handled here: the first
		// stops the run and still closes every session, and a second quits at once.
		using var cancellation = new CancellationTokenSource();
		var interruptions = 0;
		void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
		{
			e.Cancel = true;
			if (Interlocked.Increment(ref interruptions) == 1)
			{
				reporter.Interrupted();
				cancellation.Cancel();
				return;
			}

			appiumService.Dispose();
			AnsiConsole.Cursor.Show();
			Environment.Exit(RunReporter.InterruptedExitCode);
		}

		Console.CancelKeyPress += OnCancelKeyPress;

		// The display owns the console for the run's duration, so the server's output has nowhere to go
		// and is kept here instead, to be printed only if the run ends in a way nothing else explains.
		var appiumOutput = new Queue<string>();
		appiumService.OutputDataReceived += (_, args) => RecordAppiumOutput(appiumOutput, args.Data);
		appiumService.ErrorDataReceived += (_, args) => RecordAppiumOutput(appiumOutput, args.Data);
		appiumService.Start();

		// Each fixture brings its own device up and then runs on it, all inside the reporter's display
		// — so the table is on screen from the first moment, showing devices starting rather than
		// leaving the reader watching a spinner until the slowest one is ready.
		// The runtime tears the process down without unwinding, so an exception that reaches it skips
		// the summary below and the Appium server's disposal.
		var cutShort = false;
		Exception? faultBeforeInterruption = null;
		try
		{
			await reporter
				.RunAsync(() =>
					Task.WhenAll(
						selected
							.GroupBy(testCase => testCase.Config)
							.Select(group =>
								Task.Run(() =>
								{
									try
									{
										StartAndRunFixture(
											group.Key,
											[.. group],
											config.Backend,
											options.WaitBudget,
											reporter,
											manager,
											cancellation.Token
										);
									}
									catch (Exception ex) when (!cancellation.IsCancellationRequested)
									{
										_ = Interlocked.CompareExchange(ref faultBeforeInterruption, ex, null);
										throw;
									}
								})
							)
					)
				)
				.ConfigureAwait(false);
		}
		// A fixture still bringing its devices up stops by throwing, and its journeys go unreported. A
		// fault from before the interruption is still reported: the rest of the run is what hid it.
		catch (Exception) when (cancellation.IsCancellationRequested && faultBeforeInterruption is null) { }
		catch (Exception ex)
		{
			var fault = faultBeforeInterruption ?? ex;
			cutShort = true;
			RunReporter.Fault($"The run was cut short by {fault.GetType().Name}: {fault.Message}");
			ReportAppiumOutput(appiumOutput);
		}

		// Nothing after the fan-out drives a device, so a Ctrl+C from here on can take its usual effect
		// without orphaning the server.
		Console.CancelKeyPress -= OnCancelKeyPress;
		appiumService.Dispose();

		var exitCode = reporter.Summarize();
		if (cancellation.IsCancellationRequested)
		{
			return exitCode;
		}

		if (IsInteractive)
		{
			FailureBrowser.Browse(config, reporter.Failures);
		}

		return cutShort ? 1 : exitCode;
	}

	/// <summary>One of a fixture's devices and the worker index it reports under.</summary>
	/// <param name="Config">The platform fixture.</param>
	/// <param name="Index">The worker's 1-based index within the fixture.</param>
	/// <param name="DeviceId">The simulator UDID or emulator serial it runs on.</param>
	/// <param name="BackendUrlVariable">
	/// Name the app reads its backend URL from, kept so a replacement session can be opened on the
	/// same terms as the first.
	/// </param>
	/// <param name="Timings">The fixture's timing record, which every session on the worker writes to.</param>
	private sealed record Worker(
		PlatformConfig Config,
		int Index,
		string DeviceId,
		string BackendUrlVariable,
		FixtureTimings Timings
	);

	/// <summary>
	/// Orders a fixture's journeys longest first, so that a long one is never picked up last and
	/// left running alone while the fixture's other devices sit idle.
	/// </summary>
	/// <param name="cases">The journeys selected for one fixture.</param>
	internal static IEnumerable<TestCase> LongestFirst(IEnumerable<TestCase> cases) =>
		cases.OrderByDescending(testCase => testCase.Journey.ExpectedStepLocations().Count());

	/// <summary>
	/// Brings one fixture's devices up and runs its journeys across them, each device a worker
	/// pulling the next journey from a shared queue. Abandons the fixture, rather than the run, when
	/// no device can host the suite.
	/// </summary>
	/// <param name="config">The platform fixture to bring up.</param>
	/// <param name="cases">The journeys selected for it.</param>
	/// <param name="backendSetup">The suite's backend, or <c>null</c> for an app that needs none.</param>
	/// <param name="waitBudget">How long any one wait for the app may take before its step fails.</param>
	/// <param name="reporter">Told what each worker is doing, and when the fixture has to be abandoned.</param>
	/// <param name="manager">Screenshot storage the drivers write through.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	private static void StartAndRunFixture(
		PlatformConfig config,
		IReadOnlyList<TestCase> cases,
		FrameworkConfig.BackendSetup? backendSetup,
		TimeSpan waitBudget,
		RunReporter reporter,
		ScreenshotManager manager,
		CancellationToken cancellationToken
	)
	{
		// Started before the devices are, so the fixture's time covers bringing them up: that is part
		// of what a run with more devices per fixture costs.
		var timings = new FixtureTimings(waitBudget);
		try
		{
			IReadOnlyList<string> deviceIds;
			try
			{
				deviceIds = config.StartDevices(DeviceReadyTimeout, cancellationToken);
			}
			catch (InvalidOperationException ex)
			{
				reporter.FixtureSkipped(config, cases.Count, $"its devices could not be started: {ex.Message}");
				return;
			}

			var queue = new ConcurrentQueue<TestCase>(LongestFirst(cases));
			var started = 0;
			var lost = new string?[deviceIds.Count];
			var backendUrlVariable = backendSetup?.UrlVariable ?? string.Empty;
			// Each worker minds the token itself and closes its own session on the way out, so the fixture
			// waits for all of them rather than leaving one mid-cleanup when the run is interrupted.
			Task.WaitAll([
				.. deviceIds.Select(
					(deviceId, index) =>
						Task.Run(() =>
						{
							var worker = new Worker(config, index + 1, deviceId, backendUrlVariable, timings);
							var driver = StartWorker(
								worker,
								backendSetup,
								reporter,
								manager,
								cancellationToken,
								out var failure
							);
							if (driver is null)
							{
								// Once the run is interrupted, a worker that did not come up was stopped by it,
								// and reporting that as a lost device would blame the device.
								cancellationToken.ThrowIfCancellationRequested();
								lost[index] = failure;
								reporter.WorkerLost(config, worker.Index, failure);
								return;
							}

							_ = Interlocked.Increment(ref started);
							lost[index] = RunWorker(worker, driver, queue, reporter, manager, cancellationToken);
						})
				),
			]);

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			if (started == 0)
			{
				reporter.FixtureSkipped(config, cases.Count, Verdict("none of its devices could host the app", lost));
			}
			else if (!queue.IsEmpty)
			{
				reporter.FixtureSkipped(config, queue.Count, Verdict("every device it had was lost", lost));
			}
		}
		finally
		{
			reporter.FixtureFinished(config, timings);
		}
	}

	/// <summary>
	/// Words a fixture's abandonment from its workers' losses: a lone worker's reason stands on its
	/// own, and several are listed under the summary.
	/// </summary>
	/// <param name="summary">What happened to the fixture as a whole.</param>
	/// <param name="lost">Why each worker is gone, by index.</param>
	private static string Verdict(string summary, string?[] lost) =>
		lost.Length == 1
			? lost[0] ?? string.Empty
			: $"{summary}:\n" + string.Join('\n', lost.Select((reason, index) => $"worker {index + 1}: {reason}"));

	/// <summary>
	/// Brings one worker's session up with its backend attached.
	/// </summary>
	/// <param name="worker">The worker to bring up.</param>
	/// <param name="backendSetup">The suite's backend, or <c>null</c> for an app that needs none.</param>
	/// <param name="reporter">Told when the worker is up, or retrying.</param>
	/// <param name="manager">Screenshot storage the driver writes through.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	/// <param name="failure">Why the worker could not be brought up, ready to print; empty on success.</param>
	/// <returns>The live session, or <c>null</c> when the worker could not be brought up.</returns>
	private static TestDriver? StartWorker(
		Worker worker,
		FrameworkConfig.BackendSetup? backendSetup,
		RunReporter reporter,
		ScreenshotManager manager,
		CancellationToken cancellationToken,
		out string failure
	)
	{
		var driver = TryStartSession(worker, manager, reporter, cancellationToken, out failure);
		if (driver is null)
		{
			return null;
		}

		if (driver.IsAppCrashed())
		{
			var crashLog = driver.CaptureDeviceCrashLog() ?? "No crash log available.";
			QuitDriver(driver, cancellationToken);
			failure =
				"the app crashed on startup. "
				+ (
					crashLog.Contains(MissingAssembliesMarker, StringComparison.Ordinal)
						? "Rebuild with -p:EmbedAssemblies=true to embed assemblies into the APK."
						: $"Crash log:\n{crashLog}"
				);
			return null;
		}

		// The backend is built after the session, not before it, because it may have to bind itself to
		// the device it serves and only a live session names that device.
		try
		{
			driver.Backend = backendSetup?.Create(worker.Config, driver.GetDeviceId());
		}
		catch (Exception ex)
		{
			QuitDriver(driver, cancellationToken);
			failure = $"its backend failed to start: {ex.Message}";
			return null;
		}

		reporter.FixtureReady(worker.Config, worker.Index);
		return driver;
	}

	/// <summary>
	/// Opens a worker's Appium session, retrying. A session started against a device that has only
	/// just come up races the tail of its boot, and the failure that produces is transient — waiting
	/// for the device to finish and asking again costs one attempt and saves the whole worker. A
	/// device that has stopped answering does not recover by being waited on, so it is restarted
	/// instead, at most once — see <see cref="ShouldRestartDevice"/>.
	/// </summary>
	/// <param name="worker">The worker to open a session for.</param>
	/// <param name="manager">Screenshot storage the driver writes through.</param>
	/// <param name="reporter">Told when an attempt failed and another is coming.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	/// <param name="error">Why every attempt failed, ready to print; empty on success.</param>
	/// <returns>The live session, or <c>null</c> when it could not be opened.</returns>
	/// <exception cref="OperationCanceledException">The run was interrupted.</exception>
	private static TestDriver? TryStartSession(
		Worker worker,
		ScreenshotManager manager,
		RunReporter reporter,
		CancellationToken cancellationToken,
		out string error
	)
	{
		error = string.Empty;
		Exception? lastFailure = null;
		var restartTried = false;
		var restarted = false;
		for (var attempt = 1; attempt <= SessionStartAttempts; attempt++)
		{
			// Before the first attempt, not only between them: the retries exist to survive a session
			// that fails, not to stand in for bringing the device up.
			var readiness = worker.Config.WaitUntilDeviceIsReady(
				worker.DeviceId,
				DeviceReadyTimeout,
				cancellationToken
			);

			// The wait returns without looking at the token once the device is ready, and a restart
			// the reader has already stopped the run for would take the device down for nothing.
			cancellationToken.ThrowIfCancellationRequested();
			if (!restartTried && ShouldRestartDevice(readiness, attempt == SessionStartAttempts, lastFailure))
			{
				restartTried = true;

				// Said before the restart, not after it: the restart takes the device down and waits
				// out its whole boot, minutes in which the worker would otherwise say nothing.
				if (worker.Config.CanRestartDevice)
				{
					reporter.FixtureRetrying(
						worker.Config,
						worker.Index,
						readiness == PlatformConfig.DeviceReadiness.Unresponsive
							? "its device stopped responding — restarting it and waiting for it to boot"
							: "restarting its device for the last attempt, and waiting for it to boot"
					);
				}

				restarted = worker.Config.RestartDevice(worker.DeviceId, DeviceReadyTimeout);
				if (restarted)
				{
					_ = worker.Config.WaitUntilDeviceIsReady(worker.DeviceId, DeviceReadyTimeout, cancellationToken);
				}
			}

			try
			{
				return new TestDriver(
					worker.Config.CreateAppiumDriver(worker.DeviceId),
					worker.Config,
					manager,
					worker.BackendUrlVariable,
					worker.Timings,
					cancellationToken
				);
			}
			catch (Exception ex)
				when (ex is WebDriverException or FileNotFoundException or TimeoutException
					&& !cancellationToken.IsCancellationRequested
				)
			{
				lastFailure = ex;
				error = restarted
					? $"the Appium session failed to start after {SessionStartAttempts} attempts, even on a freshly restarted device, so the device is not fit to run on: {ex.Message}"
					: $"the Appium session failed to start after {SessionStartAttempts} attempts: {ex.Message}";
				if (attempt < SessionStartAttempts)
				{
					reporter.FixtureRetrying(worker.Config, worker.Index, $"attempt {attempt} failed: {ex.Message}");
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Decides whether a session attempt should be made on a freshly restarted device. A device that
	/// has stopped answering gets one straight away. One that looks ready, or is merely slow to boot,
	/// gets one only for the last attempt, and only when the earlier attempts failed in a way the
	/// device can be behind — a missing app binary fails the same on any device.
	/// </summary>
	/// <param name="readiness">What the readiness wait before this attempt found.</param>
	/// <param name="isLastAttempt">Whether this is the last attempt before the worker is lost.</param>
	/// <param name="lastFailure">Why the previous attempt failed, or <c>null</c> before the first.</param>
	/// <returns>Whether to restart the device before this attempt.</returns>
	internal static bool ShouldRestartDevice(
		PlatformConfig.DeviceReadiness readiness,
		bool isLastAttempt,
		Exception? lastFailure
	) =>
		readiness == PlatformConfig.DeviceReadiness.Unresponsive
		|| (isLastAttempt && lastFailure is WebDriverException or TimeoutException);

	/// <summary>
	/// Runs journeys from the fixture's queue on one worker until the queue is empty, replacing the
	/// session when the device stops answering it. The device's automation process can die under a
	/// journey while the app and the Appium server both stay up, and every command after that fails
	/// the same way, so a session outliving its device turns every journey the worker takes into a
	/// failure that describes nothing. A worker that cannot replace its session, or has replaced it
	/// too often, puts the journey it holds back on the queue and drops out.
	/// </summary>
	/// <param name="worker">The worker to run on.</param>
	/// <param name="driver">Its live session, with the backend attached.</param>
	/// <param name="queue">The fixture's journeys still to run, shared with its other workers.</param>
	/// <param name="reporter">Told each journey's outcome, and when the worker is lost.</param>
	/// <param name="manager">Screenshot storage the driver writes through.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	/// <returns>Why the worker dropped out, ready to print, or <c>null</c> when it drained the queue.</returns>
	private static string? RunWorker(
		Worker worker,
		TestDriver driver,
		ConcurrentQueue<TestCase> queue,
		RunReporter reporter,
		ScreenshotManager manager,
		CancellationToken cancellationToken
	)
	{
		var backend = driver.Backend;
		var recoveriesLeft = SessionRecoveryAttempts;
		var freezesLeft = NotRespondingRetries;
		var live = driver;
		try
		{
			worker.Config.OnBeforeTests(driver, driver.GetDeviceId());

			while (!cancellationToken.IsCancellationRequested && queue.TryDequeue(out var testCase))
			{
				if (RunJourney(live, testCase) is not { } result)
				{
					return null;
				}

				var sessionDied = !result.Passed && !live.IsSessionAlive();

				// Asked of a live session only, and only about a journey that failed: the dialog is
				// the system's, it sits over whatever the app was showing, and an expectation waiting
				// for an alert accepts it — so the journey reports a frozen device as a broken app.
				var frozen = !result.Passed && !sessionDied && live.ClearNotRespondingDialog();
				if (!sessionDied && !frozen)
				{
					reporter.JourneyCompleted(result);
					continue;
				}

				if (sessionDied && recoveriesLeft == 0)
				{
					queue.Enqueue(testCase);
					return Lose(
						$"its session died {SessionRecoveryAttempts} times, so its device is not fit to run on."
					);
				}

				if (frozen && freezesLeft == 0)
				{
					// Put back rather than failed, like a worker that has lost its session: the
					// journey has still not been run against a device in a state to run it, and a
					// healthy worker of the same fixture may yet take it.
					queue.Enqueue(testCase);
					return Lose(
						$"the app stopped responding on it during {NotRespondingRetries + 1} journeys, so its device "
							+ "is not fit to run on — the machine is most likely driving more devices than it can."
					);
				}

				if (sessionDied)
				{
					recoveriesLeft--;
					if (ReplaceSession() is not { } replacement)
					{
						live = null;
						queue.Enqueue(testCase);
						return Lose("its session died and would not reopen.");
					}

					live = replacement;
				}
				else
				{
					freezesLeft--;
				}

				reporter.FixtureRetrying(
					worker.Config,
					worker.Index,
					frozen
						? $"the app stopped responding under {testCase.Journey.Name}, so it runs again"
						: $"its session died under {testCase.Journey.Name}, so it runs again on a new one"
				);

				// The journey ran against a device that was not in a state to run it, so its result
				// describes the device rather than the app.
				if (RunJourney(live, testCase) is not { } rerun)
				{
					return null;
				}

				reporter.JourneyCompleted(rerun);
			}

			return null;
		}
		finally
		{
			if (live is not null)
			{
				QuitDriver(live, cancellationToken);
			}

			try
			{
				backend?.Dispose();
			}
			catch (Exception ex)
			{
				RunReporter.Note(
					$"{worker.Config} worker {worker.Index}: the backend did not shut down cleanly — {ex.Message}"
				);
			}
		}

		string Lose(string reason)
		{
			reporter.WorkerLost(worker.Config, worker.Index, reason);
			return reason;
		}

		// Returns the journey's outcome, or null once the reader has interrupted the run.
		JourneyResult? RunJourney(TestDriver on, TestCase testCase)
		{
			try
			{
				return JourneyRunner.Run(on, worker.Index, testCase, manager, reporter, cancellationToken);
			}
			catch when (cancellationToken.IsCancellationRequested)
			{
				return null;
			}
			catch (Exception ex)
			{
				// Infrastructure failures (Appium server unreachable, driver crash, etc.) must fail
				// the journey rather than escape to the runtime — an unhandled exception here would
				// SIGABRT the process and skip the finally block that disposes the Appium server,
				// orphaning its child process.
				return new JourneyResult(testCase, false, TimeSpan.Zero, ex.Message, ex);
			}
		}

		// Closes the dead session and opens another on the same device, or returns null when the
		// device will not take one. The backend is bound to the device rather than to the session,
		// so the replacement inherits it.
		TestDriver? ReplaceSession()
		{
			QuitDriver(live, cancellationToken);
			var replacement = TryStartSession(worker, manager, reporter, cancellationToken, out var error);
			if (replacement is null)
			{
				RunReporter.Note($"{worker.Config} worker {worker.Index}: {error}");
				return null;
			}

			replacement.Backend = backend;
			try
			{
				worker.Config.OnBeforeTests(replacement, replacement.GetDeviceId());
			}
			catch
			{
				// Not yet the worker's live session, so the worker's own cleanup would not close it.
				QuitDriver(replacement, cancellationToken);
				throw;
			}

			return replacement;
		}
	}

	/// <summary>
	/// Keeps the newest line the Appium server wrote, dropping the oldest once the tail is full.
	/// Called from the server's own reader threads.
	/// </summary>
	/// <param name="output">The tail collected so far.</param>
	/// <param name="line">What the server wrote, or <c>null</c> at the end of the stream.</param>
	private static void RecordAppiumOutput(Queue<string> output, string? line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return;
		}

		lock (output)
		{
			output.Enqueue(line);
			if (output.Count > AppiumOutputTailLines)
			{
				_ = output.Dequeue();
			}
		}
	}

	/// <summary>
	/// Prints what the Appium server said, for an ending the run's own artifacts cannot account for.
	/// The output goes nowhere else, so this is the only chance to read it.
	/// </summary>
	/// <param name="output">The tail of the server's output.</param>
	private static void ReportAppiumOutput(Queue<string> output)
	{
		string[] lines;
		lock (output)
		{
			lines = [.. output];
		}

		if (lines.Length == 0)
		{
			return;
		}

		RunReporter.Note($"The Appium server's last {lines.Length} lines:\n{string.Join('\n', lines)}");
	}

	/// <summary>
	/// Closes a fixture's session. Its journeys are already reported by this point, so a session that
	/// cannot be closed is said out loud and does not fail the run.
	/// </summary>
	/// <param name="driver">The session to close.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	private static void QuitDriver(TestDriver driver, CancellationToken cancellationToken)
	{
		try
		{
			driver.App.Quit();
			driver.App.Dispose();
		}
		catch when (cancellationToken.IsCancellationRequested)
		{
			// Appium HTTP calls may fail when interrupted.
		}
		catch (WebDriverException ex)
		{
			RunReporter.Note($"{driver.Config}: the Appium session did not close cleanly — {ex.Message}");
		}
	}

	/// <summary>
	/// Whether the console is a terminal a reader is watching, and so can host a display that
	/// redraws in place. Redirected output has no cursor to move, and a display that redraws into it
	/// writes the whole thing again several times a second.
	/// </summary>
	internal static bool IsInteractive { get; } =
		!Console.IsOutputRedirected
		&& !Console.IsInputRedirected
		&& Environment.GetEnvironmentVariable("NO_COLOR") is null
		&& AnsiConsole.Profile.Capabilities.Interactive;
}
