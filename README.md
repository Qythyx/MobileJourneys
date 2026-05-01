# MobileJourneys

A reusable .NET 10 UI-test framework for iOS and Android apps, built on [Appium](https://appium.io/)
and
[Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro).
Tests are written as **journeys** — declarative sequences of `Action` and `Expectation` records —
and verified against per-platform PNG screenshot baselines.

[![CI](https://github.com/Qythyx/MobileJourneys/actions/workflows/ci.yml/badge.svg)](https://github.com/Qythyx/MobileJourneys/actions/workflows/ci.yml)

## What you get

- A small DSL (`JourneyAction`, `Expectation`, `JourneyStep`, `JourneyDefinition`) for expressing
  app flows declaratively.
- Built-in actions: `Tap`, `TypeText`, `SwipeLeft`/`Right`, `ScrollToElement`, `DismissAlert`,
  `DismissKeyboard`, `TapAlertButton`, `TapNotification`, `InvertSystemTheme`, `SetSystemFontSize`,
  `None`.
- Built-in expectations: `Found`, `NotFound`, `FoundWithText`, `AlertFound`,
  `WaitForNotification`.
- An Appium-driven `TestDriver` with element-finding (with stale-element retry), gestures, alerts,
  deep links, hardware-keyboard control, and crash detection.
- Screenshot-baseline comparison via `SixLabors.ImageSharp` + `Codeuctivity.ImageSharpCompare` with
  maskable regions for animated UI elements.
- A custom MTP `TestFramework` that runs the cross-product of platform fixtures and journeys, with
  `--filter`, `--rerun`, `--list-extraneous`, `--delete-extraneous` CLI flags.

## External dependencies

Beyond the .NET SDK, MobileJourneys drives real simulators/emulators via several external tools.
**`DependencyChecker.Verify(config)` runs at the start of every test session** and fails fast (with
an install hint) if any required tool is missing — only the platforms you've configured in
`FrameworkConfig.PlatformConfigs` are checked.

| Tool                                | Used for                                                   | When required                                | Install                                                                                                                                            |
| ----------------------------------- | ---------------------------------------------------------- | -------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Appium 2.x](https://appium.io/)    | Driving iOS / Android via WebDriver                        | Always                                       | `npm install -g appium` (and `node`/`npm` on PATH)                                                                                                 |
| Xcode command-line tools (`xcrun`)  | iOS simulator boot, theme/font, screenshots, push payloads | If any `IosPlatformConfig` is configured     | `xcode-select --install`                                                                                                                           |
| Android SDK platform-tools (`adb`)  | Android emulator control, theme/font, logcat               | If any `AndroidPlatformConfig` is configured | Android Studio → SDK Manager → "Android SDK Platform-Tools", or [standalone download](https://developer.android.com/tools/releases/platform-tools) |
| `ANDROID_HOME` environment variable | Resolves `$ANDROID_HOME/platform-tools/adb`                | If any `AndroidPlatformConfig` is configured | `export ANDROID_HOME=$HOME/Library/Android/sdk` (typical macOS path)                                                                               |

A simulator/emulator must be **booted before the test session starts** (the framework attaches via
Appium; it does not boot devices itself). On macOS:

```bash
xcrun simctl list devices       # list simulators
xcrun simctl boot "iPhone 17 Pro"
emulator -avd Pixel_8_API35     # Android (in a separate terminal)
```

## Quick start

The framework is consumed by an MTP-based test executable that supplies its own journeys, platform
fixtures, and per-app mock state.

### 1. Reference the project

The framework currently ships as source — reference it via `<ProjectReference>` to a sibling
checkout. NuGet packaging is on the roadmap.

```xml
<ProjectReference Include="../../../MobileJourneys/MobileJourneys/MobileJourneys.csproj" />
```

### 2. Implement `IJourneyEnvironment`

Define a record that captures your app's mock state (the fields each journey can override) and
converts them to environment variables your app's mock service reads. `ForFixture` lets you
specialize the environment per-platform fixture (e.g., pin language to match the fixture's theme).

```csharp
public record MyAppEnvironment : IJourneyEnvironment
{
    public bool LoggedIn { get; init; } = true;
    public bool ShowOnboarding { get; init; }
    public string Name { get; init; } = "Default";

    public IReadOnlyDictionary<string, string> GetEnvVars() =>
        // Convert your properties to env vars your app's mock service reads,
        // e.g., MOCK_LOGGED_IN=true MOCK_SHOW_ONBOARDING=false.
        new Dictionary<string, string>
        {
            ["MOCK_LOGGED_IN"] = LoggedIn ? "true" : "false",
            ["MOCK_SHOW_ONBOARDING"] = ShowOnboarding ? "true" : "false",
        };

    public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
}
```

### 3. Define platform fixtures

```csharp
public static class MyAppPlatforms
{
    private const string AppId = "com.example.myapp";

    public static readonly IReadOnlyList<PlatformConfig> All =
    [
        new IosPlatformConfig(
            PlatformVersion: "26.2",
            DeviceName: "iPhone 17 Pro",
            IsLightTheme: true,
            AppIdentifier: AppId,
            AppBinaryPath: Path.Combine(TestAssembly.RepoRoot, "src/MyApp.app"),
            MaxScreenshotHeight: 2000),
        new AndroidPlatformConfig(
            PlatformVersion: "15",
            DeviceName: "Pixel 8",
            AvdName: "Pixel_8_API35",
            IsLightTheme: true,
            AppIdentifier: AppId,
            AppBinaryPath: Path.Combine(TestAssembly.RepoRoot, "src/MyApp-Signed.apk"),
            MainActivity: null,
            MaxScreenshotHeight: 2000),
    ];
}
```

### 4. Write journeys

```csharp
public static class Journeys
{
    public static readonly JourneyDefinition Login = new(
        new MyAppEnvironment { LoggedIn = false },
        [new Found("LoginButton")],
        [
            new(new Tap("LoginButton"), [new Found("EmailField")]),
            new(new TypeText("EmailField", "test@example.com")),
            new(new TypeText("PasswordField", "hunter2")),
            new(new Tap("SubmitButton"), [new Found("HomeScreen")]),
        ]);

    public static IEnumerable<JourneyDefinition> All => [Login /*, …*/];
}
```

### 5. Wire up `Program.Main`

```csharp
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var config = new FrameworkConfig(
            "MyApp UI Tests",
            "UI test runner for MyApp.",
            "MyApp.UITests.Journeys",
            "myapp",
            MyAppPlatforms.All,
            [.. Journeys.All]
        );

        if (args.Contains($"--{CommandLineProvider.ListExtraneousOption}") ||
            args.Contains($"--{CommandLineProvider.DeleteExtraneousOption}")
        )
        {
            var deleteMode = args.Contains($"--{CommandLineProvider.DeleteExtraneousOption}");
            return CheckExtraneousFiles(config, deleteMode);
        }

        var builder = await TestApplication.CreateBuilderAsync(args);
#if RUN_UI_TESTS
        builder.AddSelfRegisteredExtensions(args);
#endif
        builder.CommandLine.AddProvider(() => new CommandLineProvider(config));
        _ = builder.RegisterTestFramework(
            _ => new TestFrameworkCapabilities(),
            (caps, sp) => new TestFramework(caps, sp, config)
        );
        using var app = await builder.BuildAsync();
        return await app.RunAsync();
    }

    private static int CheckExtraneousFiles(FrameworkConfig config, bool delete)
    {
        var storage = config.Storage ?? FilesystemScreenshotStorage.Default();
        var paths = storage.FindExtraneous(config, j => j.ExpectedStepNames(), delete);
        Console.WriteLine($"{(delete ? "Deleted" : "Found")} {paths.Count} extraneous file(s).");
        foreach (var p in paths) Console.WriteLine($"  {p}");
        return delete ? 0 : (paths.Count == 0 ? 0 : 1);
    }
}
```

`ScreenshotManager` is an instance class that consumes a `ScreenshotStorage`. The default is
`FilesystemScreenshotStorage` rooted at the test project's `Screenshots/` directory; override via
`FrameworkConfig { Storage = … }` to swap in an alternative backend.

### 6. Required csproj bits

The consumer csproj must:

```xml
<PropertyGroup>
    <UseMaui>true</UseMaui> <!-- if your app is MAUI -->
    <OutputType>Exe</OutputType>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <IsTestingPlatformApplication Condition="'$(RunUITests)' == 'true'">true</IsTestingPlatformApplication>
    <GenerateTestingPlatformEntryPoint>false</GenerateTestingPlatformEntryPoint>
</PropertyGroup>
<ItemGroup>
    <!-- Required: lets TestAssembly resolve ProjectDir / RepoRoot. -->
    <AssemblyMetadata Include="ProjectDir" Value="$(MSBuildProjectDirectory)" />
    <AssemblyMetadata Include="RepoRoot" Value="$(RepoRoot)" />
</ItemGroup>
<ItemGroup>
    <PackageReference Include="Microsoft.Testing.Platform" />
    <PackageReference Include="Microsoft.Testing.Platform.MSBuild" />
</ItemGroup>
```

## Running

```bash
# All journeys, all platforms (gated behind RunUITests=true so solution-wide test runs skip them)
dotnet test --project test/MyApp.UITests/MyApp.UITests.csproj -p:RunUITests=true -- --filter ""

# A single journey on iOS light fixture (substring match against "{platform}.{journey}")
dotnet test --project test/MyApp.UITests/MyApp.UITests.csproj -p:RunUITests=true -- \
    --filter Login --filter "iPhone 17 Pro"

# Re-run only journeys with failure artifacts on disk
dotnet test --project test/MyApp.UITests/MyApp.UITests.csproj -p:RunUITests=true -- --rerun

# Maintenance: detect / clean up orphaned baselines after journey renames
dotnet run --project test/MyApp.UITests -p:RunUITests=true -- --list-extraneous
dotnet run --project test/MyApp.UITests -p:RunUITests=true -- --delete-extraneous
```

## Screenshots

Baselines live under
`<consumer-project>/Screenshots/<PlatformConfig.DisplayName>/<JourneyName>/<NN> <StepLabel>.png`.
The first run for a new step produces the baseline; subsequent runs compare. Failure artifacts (the
actual capture for a mismatch, the diff visualization, FAIL screenshots from exceptions, and crash
logs) land alongside the baseline and are auto-cleaned when the step next passes. Filename layout is
owned by the storage backend; with the default `FilesystemScreenshotStorage` they appear as
`<step>.new.png`, `<step>_diff_<pct>%.png`, `<step>_FAIL_<reason>.png`, and `<step>.CRASH.txt`.

## Custom actions and expectations

Subclass `JourneyAction` (override `Execute(TestDriver)`) or `Expectation` (override
`Verify(TestDriver)`). Override the protected `Name` property if you want a wrapper class to share
its parent's baseline filename:

```csharp
public sealed record TapLocalizedAlert(string English, string Japanese) : JourneyAction(English)
{
    protected override string Name => nameof(TapAlertButton); // baseline says "TapAlertButton …"
    public override void Execute(TestDriver driver) =>
        new TapAlertButton(driver.Config.IsLightTheme ? English : Japanese).Execute(driver);
}
```

## Worked example

The Beerbox app (`~/Projects/beerbox`) is the original consumer this framework was extracted from —
see `test/app/Beerbox.App.UITests/` there for a complete production example: `BeerboxPlatforms.cs`,
`MockEnvironment.cs`, the 6 Beerbox-specific actions, and 32 journeys.

## Development

### Running the unit tests

The unit test suite (`MobileJourneys.Tests`) is plain NUnit on top of Microsoft.Testing.Platform —
no simulators or external tools are required to run it.

```bash
dotnet test
```

### Code coverage

Coverage is collected by
[Microsoft.Testing.Extensions.CodeCoverage](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-code-coverage)
(the MTP-native collector — `coverlet.collector` is not compatible with MTP) and rendered to HTML by
[ReportGenerator](https://github.com/danielpalme/ReportGenerator), pinned as a local
`dotnet-tools.json` tool.

```bash
./scripts/coverage.sh
```

This restores the local tools, runs the tests with coverage, generates an HTML report at
`coverage/index.html`, and opens it on macOS. Test assemblies are excluded by default (MTP sets
`IncludeTestAssembly=false`) and the underlying tool honors `[ExcludeFromCodeCoverage]` without
extra config. Add a `coverage.runsettings` file (`<Configuration><CodeCoverage>...`) and pass it via
`--coverage-settings` if you ever need finer-grained exclusions.

CI runs the same flow on every push and PR ([.github/workflows/ci.yml](.github/workflows/ci.yml)),
publishes a coverage summary to the GitHub Actions job page, and uploads the full HTML report as a
build artifact (`coverage-report`).

## Status

- v0: ProjectReference-only consumption from sibling repos.
- v1 (planned): NuGet package on a public feed.
