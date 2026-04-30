using Microsoft.Testing.Platform.Capabilities.TestFramework;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Requests;
using Microsoft.Testing.Platform.Services;
using Microsoft.Testing.Platform.TestHost;
using OpenQA.Selenium.Appium.Service;
using OpenQA.Selenium.Appium.Service.Options;

namespace MobileJourneys.Framework;

/// <summary>
/// The Microsoft.Testing.Platform <see cref="ITestFramework"/> implementation. Discovers
/// the cross-product of <see cref="FrameworkConfig.PlatformConfigs"/> and
/// <see cref="FrameworkConfig.Journeys"/>, runs each platform group's tests concurrently,
/// and publishes lifecycle events to MTP via <see cref="MtpReporter"/>.
/// </summary>
/// <param name="capabilities">MTP capabilities (unused but required by the contract).</param>
/// <param name="serviceProvider">MTP service provider; used to read CLI options.</param>
/// <param name="config">The framework configuration the consumer's Program.Main built.</param>
public sealed class TestFramework(
	ITestFrameworkCapabilities capabilities,
	IServiceProvider serviceProvider,
	FrameworkConfig config
) : ITestFramework, IDataProducer
{
	private const string AppiumHostAddress = "127.0.0.1";

	private const int AppiumHostPort = 4723;

	/// <inheritdoc/>
	public string Uid => TestAssembly.Name;

	/// <inheritdoc/>
	public string Version => TestAssembly.FrameworkVersion;

	/// <inheritdoc/>
	public string DisplayName => config.DisplayName;

	/// <inheritdoc/>
	public string Description => config.Description;

	/// <inheritdoc/>
	public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];

	/// <inheritdoc/>
	public Task<bool> IsEnabledAsync() => Task.FromResult(true);

	/// <inheritdoc/>
	public Task<CreateTestSessionResult> CreateTestSessionAsync(CreateTestSessionContext context) =>
		Task.FromResult(new CreateTestSessionResult { IsSuccess = true });

	/// <inheritdoc/>
	public Task<CloseTestSessionResult> CloseTestSessionAsync(CloseTestSessionContext context) =>
		Task.FromResult(new CloseTestSessionResult { IsSuccess = true });

	/// <inheritdoc/>
	public async Task ExecuteRequestAsync(ExecuteRequestContext context)
	{
		_ = capabilities;
		var sessionUid = context.Request.Session.SessionUid;

		switch (context.Request)
		{
			case DiscoverTestExecutionRequest:
				foreach (var testCase in TestCases)
				{
					await PublishDiscoveredAsync(context, sessionUid, testCase);
				}
				break;

			case RunTestExecutionRequest:
				await RunAsync(context, sessionUid);
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
		// Fail fast on missing external deps (Appium, xcrun, adb) with an install hint
		// instead of producing cryptic Appium errors mid-session.
		DependencyChecker.Verify(config);

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
			await reporter.TestsSkippedAsync("Skipped UI tests due to filters or rerun", $"Skipped {skipped} tests");
		}
		if (selected.Count == 0)
		{
			return;
		}

		using var appiumService = new AppiumServiceBuilder()
			.WithIPAddress(AppiumHostAddress)
			.UsingPort(AppiumHostPort)
			.WithArguments(new OptionCollector().AddArguments(new("--allow-insecure", "*:adb_shell")))
			.Build();
		appiumService.Start();

		await Task.WhenAll(
			selected
				.GroupBy(tc => tc.Config)
				.Select(group =>
					Task.Run(
						async () =>
						{
							var driver = group.Key.GetTestDriver(config.DeepLinkScheme);
							var cases = (IReadOnlyList<TestCase>)[.. group];
							if (driver.IsAppCrashed())
							{
								var crashLog = driver.CaptureDeviceCrashLog() ?? "No crash log available.";
								await reporter.TestsSkippedAsync(
									$"Skipped UI tests for {driver.Config}",
									$"Skipped {cases.Count} tests because app crashed on startup. "
										+ (
											crashLog.Contains("No assemblies found")
												? "Rebuild with -p:EmbedAssemblies=true to embed assemblies into the APK."
												: $"Crash log:\n{crashLog}"
										)
								);
								return;
							}

							foreach (var testCase in cases)
							{
								await PublishDiscoveredAsync(context, sessionUid, testCase);
							}

							await RunTestCasesAsync(driver, cases, reporter, context.CancellationToken);
						},
						context.CancellationToken
					)
				)
		);
	}

	private static async Task RunTestCasesAsync(
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
			config.OnBeforeTests(driver, deviceId);

			foreach (var testCase in cases)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				await reporter.JourneyStartedAsync(testCase);
				try
				{
					var result = await JourneyRunner.RunAsync(driver, testCase, reporter);
					await reporter.JourneyCompletedAsync(result);
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
					await reporter.JourneyCompletedAsync(
						new JourneyResult(testCase, false, TimeSpan.Zero, ex.Message, ex)
					);
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
