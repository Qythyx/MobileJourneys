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

	/// <summary>How many times to try opening a fixture's Appium session before abandoning it.</summary>
	private const int SessionStartAttempts = 2;

	/// <summary>How many lines of the Appium server's output to keep back for a post-mortem.</summary>
	private const int AppiumOutputTailLines = 200;

	/// <summary>How long to give a device to finish booting before retrying its session.</summary>
	private static readonly TimeSpan DeviceReadyTimeout = TimeSpan.FromSeconds(90);

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

		using var cancellation = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) =>
		{
			// Handle it ourselves so the finally blocks below still dispose the Appium server;
			// letting the runtime kill the process orphans its child.
			e.Cancel = true;
			cancellation.Cancel();
		};

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
		var interrupted = false;
		try
		{
			await reporter
				.RunAsync(() =>
					Task.WhenAll(
						selected
							.GroupBy(testCase => testCase.Config)
							.Select(group =>
								Task.Run(
									() =>
										StartAndRunFixture(
											group.Key,
											[.. group],
											config.Backend,
											reporter,
											manager,
											cancellation.Token
										),
									cancellation.Token
								)
							)
					)
				)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			interrupted = true;
			RunReporter.Note("The run was interrupted, so the results below cover only what finished.");
		}
		catch (Exception ex)
		{
			interrupted = true;
			RunReporter.Fault($"The run was cut short by {ex.GetType().Name}: {ex.Message}");
			ReportAppiumOutput(appiumOutput);
		}

		var exitCode = reporter.Summarize();
		if (IsInteractive)
		{
			FailureBrowser.Browse(config, reporter.Failures);
		}

		return interrupted ? 1 : exitCode;
	}

	/// <summary>A fixture whose device is up, its app installed, and its journeys ready to run.</summary>
	/// <param name="Config">The platform fixture.</param>
	/// <param name="Cases">The journeys selected for it.</param>
	/// <param name="Driver">Its live Appium session.</param>
	private sealed record FixtureSession(PlatformConfig Config, IReadOnlyList<TestCase> Cases, TestDriver Driver);

	/// <summary>The outcome of trying to bring a fixture up.</summary>
	/// <param name="Config">The platform fixture.</param>
	/// <param name="Cases">The journeys selected for it.</param>
	/// <param name="Driver">Its Appium session, or <c>null</c> when it could not be brought up.</param>
	/// <param name="Failure">Why it could not be brought up, ready to print, or <c>null</c> on success.</param>
	private sealed record FixtureStart(
		PlatformConfig Config,
		IReadOnlyList<TestCase> Cases,
		TestDriver? Driver,
		string? Failure
	);

	/// <summary>
	/// Brings one fixture's device up and runs its journeys on it. Abandons the fixture, rather than
	/// the run, when the device cannot host the suite.
	/// </summary>
	/// <param name="config">The platform fixture to bring up.</param>
	/// <param name="cases">The journeys selected for it.</param>
	/// <param name="backendSetup">The suite's backend, or <c>null</c> for an app that needs none.</param>
	/// <param name="reporter">Told what the fixture is doing, and when it has to be abandoned.</param>
	/// <param name="manager">Screenshot storage the driver writes through.</param>
	/// <param name="cancellationToken">Cancelled when the reader interrupts the run.</param>
	private static void StartAndRunFixture(
		PlatformConfig config,
		IReadOnlyList<TestCase> cases,
		FrameworkConfig.BackendSetup? backendSetup,
		RunReporter reporter,
		ScreenshotManager manager,
		CancellationToken cancellationToken
	)
	{
		var start = StartFixture(
			config,
			cases,
			manager,
			backendSetup?.UrlVariable ?? string.Empty,
			reporter,
			cancellationToken
		);
		if (start.Driver is null)
		{
			reporter.FixtureSkipped(config, cases.Count, start.Failure ?? "the app did not start.");
			return;
		}

		// The backend is built after the session, not before it, because it may have to bind itself to
		// the device it serves and only a live session names that device.
		IJourneyBackend? backend;
		try
		{
			backend = backendSetup?.Create(config, start.Driver.GetDeviceId());
		}
		catch (Exception ex)
		{
			// Abandon the fixture rather than the run, as a failed session start does — and rather than
			// escaping to the runtime, which would skip disposing the Appium server.
			QuitDriver(start.Driver, cancellationToken);
			reporter.FixtureSkipped(config, cases.Count, $"its backend failed to start: {ex.Message}");
			return;
		}

		try
		{
			start.Driver.Backend = backend;
			reporter.FixtureReady(config);
			RunFixture(new FixtureSession(config, cases, start.Driver), reporter, manager, cancellationToken);
		}
		finally
		{
			try
			{
				backend?.Dispose();
			}
			catch (Exception ex)
			{
				RunReporter.Note($"{config}: the backend did not shut down cleanly — {ex.Message}");
			}
		}
	}

	private static FixtureStart StartFixture(
		PlatformConfig config,
		IReadOnlyList<TestCase> cases,
		ScreenshotManager manager,
		string backendUrlVariable,
		RunReporter reporter,
		CancellationToken cancellationToken
	)
	{
		var driver = TryStartSession(config, manager, backendUrlVariable, reporter, out var sessionError);
		if (driver is null)
		{
			return new FixtureStart(config, cases, null, sessionError);
		}

		if (!driver.IsAppCrashed())
		{
			return new FixtureStart(config, cases, driver, null);
		}

		var crashLog = driver.CaptureDeviceCrashLog() ?? "No crash log available.";
		QuitDriver(driver, cancellationToken);
		return new FixtureStart(
			config,
			cases,
			null,
			"the app crashed on startup. "
				+ (
					crashLog.Contains(MissingAssembliesMarker, StringComparison.Ordinal)
						? "Rebuild with -p:EmbedAssemblies=true to embed assemblies into the APK."
						: $"Crash log:\n{crashLog}"
				)
		);
	}

	/// <summary>
	/// Opens the Appium session, retrying once. A session started against a device that has only just
	/// come up races the tail of its boot, and the failure that produces is transient — waiting for
	/// the device to finish and asking again costs one attempt and saves the whole fixture.
	/// </summary>
	/// <param name="config">The platform fixture to open a session on.</param>
	/// <param name="manager">Screenshot storage the driver writes through.</param>
	/// <param name="backendUrlVariable">Name the app reads its backend URL from.</param>
	/// <param name="reporter">Told when an attempt failed and another is coming.</param>
	/// <param name="error">Why every attempt failed, ready to print; empty on success.</param>
	/// <returns>The live session, or <c>null</c> when it could not be opened.</returns>
	private static TestDriver? TryStartSession(
		PlatformConfig config,
		ScreenshotManager manager,
		string backendUrlVariable,
		RunReporter reporter,
		out string error
	)
	{
		error = string.Empty;
		for (var attempt = 1; attempt <= SessionStartAttempts; attempt++)
		{
			try
			{
				return new TestDriver(config.CreateAppiumDriver(), config, manager, backendUrlVariable);
			}
			catch (Exception ex) when (ex is WebDriverException or FileNotFoundException or TimeoutException)
			{
				error = $"the Appium session failed to start after {SessionStartAttempts} attempts: {ex.Message}";
				if (attempt < SessionStartAttempts)
				{
					reporter.FixtureRetrying(config, $"attempt {attempt} failed: {ex.Message}");
					config.WaitUntilDevicesAreReady(DeviceReadyTimeout);
				}
			}
		}

		return null;
	}

	private static void RunFixture(
		FixtureSession session,
		RunReporter reporter,
		ScreenshotManager manager,
		CancellationToken cancellationToken
	)
	{
		var driver = session.Driver;
		try
		{
			session.Config.OnBeforeTests(driver, driver.GetDeviceId());

			foreach (var testCase in session.Cases)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				try
				{
					reporter.JourneyCompleted(JourneyRunner.Run(driver, testCase, manager, reporter));
				}
				catch when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					// Infrastructure failures (Appium server unreachable, driver crash, etc.) must fail
					// the journey rather than escape to the runtime — an unhandled exception here would
					// SIGABRT the process and skip the finally block that disposes the Appium server,
					// orphaning its child process.
					reporter.JourneyCompleted(new JourneyResult(testCase, false, TimeSpan.Zero, ex.Message, ex));
				}
			}
		}
		finally
		{
			QuitDriver(driver, cancellationToken);
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

	private static bool IsInteractive { get; } =
		!Console.IsOutputRedirected
		&& !Console.IsInputRedirected
		&& Environment.GetEnvironmentVariable("NO_COLOR") is null
		&& AnsiConsole.Profile.Capabilities.Interactive;
}
