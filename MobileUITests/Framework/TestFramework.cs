using Microsoft.Testing.Platform.Capabilities.TestFramework;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Requests;
using Microsoft.Testing.Platform.Services;
using Microsoft.Testing.Platform.TestHost;

namespace MobileUITests.Framework;

public sealed class TestFramework(
	ITestFrameworkCapabilities capabilities,
	IServiceProvider serviceProvider,
	FrameworkConfig config
) : ITestFramework, IDataProducer
{
	public string Uid => TestAssembly.Name;

	public string Version => "1.0.0";

	public string DisplayName => config.DisplayName;

	public string Description => config.Description;

	public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];

	public Task<bool> IsEnabledAsync() => Task.FromResult(true);

	public Task<CreateTestSessionResult> CreateTestSessionAsync(CreateTestSessionContext context) =>
		Task.FromResult(new CreateTestSessionResult { IsSuccess = true });

	public Task<CloseTestSessionResult> CloseTestSessionAsync(CloseTestSessionContext context) =>
		Task.FromResult(new CloseTestSessionResult { IsSuccess = true });

	public async Task ExecuteRequestAsync(ExecuteRequestContext context)
	{
		_ = capabilities;
		var sessionUid = context.Request.Session.SessionUid;

		switch (context.Request)
		{
			case DiscoverTestExecutionRequest:
				foreach (var testCase in TestCases)
				{
					await PublishDiscoveredAsync(context, sessionUid, testCase).ConfigureAwait(false);
				}
				break;

			case RunTestExecutionRequest:
				await RunAsync(context, sessionUid).ConfigureAwait(false);
				break;
		}

		context.Complete();
	}

	private Task PublishDiscoveredAsync(ExecuteRequestContext context, SessionUid sessionUid, TestCase testCase)
	{
		var node = TestNodeFactory.Create(testCase, config.TestNodeNamespace);
		node.Properties.Add(DiscoveredTestNodeStateProperty.CachedInstance);
		return context.MessageBus.PublishAsync(this, new TestNodeUpdateMessage(sessionUid, node));
	}

	private IReadOnlyList<TestCase> TestCases =>
		field ??= [.. config.PlatformConfigs.SelectMany(c => config.Journeys.Select(j => new TestCase(c, j)))];

	private async Task RunAsync(ExecuteRequestContext context, SessionUid sessionUid)
	{
		var options = serviceProvider.GetCommandLineOptions();
		var filters = options.TryGetOptionArgumentList(CommandLineProvider.FilterOption, out var values) ? values : [];
		var rerun = options.IsOptionSet(CommandLineProvider.RerunOption);
		var selected = TestCases
			.Where(tc =>
				(filters.Length == 0 || filters.All(f => tc.Uid.Contains(f, StringComparison.OrdinalIgnoreCase)))
				&& (!rerun || FailedTestScanner.IsFailedJourney(tc.Config, tc.Journey))
			)
			.ToList();

		var reporter = new MtpReporter(context.MessageBus, sessionUid, this, config.TestNodeNamespace);
		var skipped = TestCases.Count - selected.Count;
		if (skipped > 0)
		{
			reporter.TestsSkipped("Skipped UI tests due to filters or rerun", $"Skipped {skipped} tests");
		}

		if (selected.Count == 0)
		{
			return;
		}

		AppiumServerHelper.StartAppiumLocalServer();

		var groups = selected
			.GroupBy(tc => tc.Config)
			.Select(group =>
				(new Lazy<TestDriver>(() => group.Key.GetTestDriver(config.DeepLinkScheme)), group.AsEnumerable())
			)
			.Where(tuple =>
			{
				var (lazyDriver, testCases) = tuple;
				try
				{
					var driver = lazyDriver.Value;
					if (driver.IsAppCrashed())
					{
						var crashLog = driver.CaptureDeviceCrashLog(appCrashed: true) ?? "No crash log available.";
						reporter.TestsSkipped(
							$"Skipped UI tests for {driver.Config}",
							$"Skipped {testCases.Count()} tests because app crashed on startup. "
								+ (
									crashLog.Contains("No assemblies found")
										? "Rebuild with -p:EmbedAssemblies=true to embed assemblies into the APK."
										: $"Crash log:\n{crashLog}"
								)
						);
						return false;
					}
				}
				catch when (context.CancellationToken.IsCancellationRequested)
				{
					return false;
				}
				return testCases.Any();
			})
			.ToList();

		// Pre-publish every selected test as Discovered so MTP's terminal reporter knows
		// the total count before any InProgress/Passed updates arrive.
		foreach (var testCase in groups.SelectMany(g => g.Item2))
		{
			await PublishDiscoveredAsync(context, sessionUid, testCase).ConfigureAwait(false);
		}

		try
		{
			// Each worker's body is synchronous (driver creation + JourneyRunner.Run block).
			// Task.Run hands each off to the threadpool so the platforms actually run concurrently.
			var tasks = groups.Select(group =>
				Task.Run(
					() => RunTestCases(group.Item1.Value, [.. group.Item2], reporter, context.CancellationToken),
					context.CancellationToken
				)
			);
			await Task.WhenAll(tasks).ConfigureAwait(false);
		}
		finally
		{
			AppiumServerHelper.DisposeAppiumLocalServer();
		}
	}

	private static void RunTestCases(
		TestDriver driver,
		IReadOnlyList<TestCase> cases,
		MtpReporter reporter,
		CancellationToken cancellationToken
	)
	{
		var config = driver.Config;

		try
		{
			var deviceId = driver.GetDeviceId();
			if (config.Platform == TestPlatform.iOS)
			{
				SimulatorHelper.EnableiOSHardwareKeyboard(deviceId);
				driver.DismissAlertIfPresent(TimeSpan.FromSeconds(5));
			}

			foreach (var testCase in cases)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				reporter.JourneyStarted(testCase);
				try
				{
					reporter.JourneyCompleted(JourneyRunner.Run(driver, testCase, reporter));
				}
				catch when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					// Infrastructure failures (Appium server unreachable, driver crash, etc.)
					// must fail the journey rather than escape to the runtime — an unhandled
					// exception here would SIGABRT the test process and skip the finally
					// block that disposes the Appium server, orphaning its child process.
					reporter.JourneyCompleted(new JourneyResult(testCase, false, TimeSpan.Zero, ex.Message, ex));
				}
			}
		}
		finally
		{
			try
			{
				driver.App.Quit();
				driver.App.Dispose();
			}
			catch when (cancellationToken.IsCancellationRequested)
			{
				// Appium HTTP calls may fail when interrupted; swallow on cancellation.
			}
		}
	}
}
