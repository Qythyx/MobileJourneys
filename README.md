# MobileJourneys

A reusable .NET 10 UI-test framework for iOS and Android apps, built on
[Appium](https://appium.io/). Tests are written as **journeys** — declarative sequences of `Action`
and `Expectation` records — and verified against per-platform PNG screenshot baselines. The consumer
is a plain console application run with `dotnet run`, so the framework owns its console output
rather than publishing progress to a test host.

[![CI](https://github.com/Qythyx/MobileJourneys/actions/workflows/ci.yml/badge.svg)](https://github.com/Qythyx/MobileJourneys/actions/workflows/ci.yml)

## What you get

- A small model (`JourneyAction`, `Expectation`, `JourneyStep`, `JourneyDefinition`) for expressing
  app flows declaratively — plus `JourneyTree`/`Branch` for suites of journeys sharing step
  prefixes, so shared steps are defined and screenshotted once.
- A `using static` factory DSL (`MobileJourneys.Dsl`) so journeys read as bare calls —
  `Branch("Menu", Step(Tap(id), Found(a)))` — instead of a wall of `new`.
- Built-in actions: `Tap`, `TypeText`, `SwipeLeft`/`Right`, `DismissAlert`, `DismissKeyboard`,
  `TapAlertButton`, `TapNotification`, `InvertSystemTheme`, `SetSystemFontSize`, `None`.
- Built-in expectations: `Found`, `NotFound`, `FoundWithText`, `AlertFound`, `WaitForNotification`.
  `WaitForNotification` recognises a banner by comparing the rows named by
  `NotificationBannerTop`/`NotificationBannerBottom` against an empty capture of those same rows, so
  the action that triggers the notification must call `TestDriver.CaptureEmptyBannerRegion()` after
  backgrounding the app and **before** scheduling the notification.
- An Appium-driven `TestDriver` with element-finding (with stale-element retry), gestures, alerts,
  deep links, hardware-keyboard control, and crash detection.
- Screenshot-baseline comparison via `SixLabors.ImageSharp` + `Codeuctivity.ImageSharpCompare` with
  maskable regions for animated UI elements.
- A `SuiteRunner` that runs the cross-product of platform fixtures and journeys from a plain console
  entry point, with `--run`, `--journey`, `--filter`, `--rerun`, `--report-to`,
  `--list-extraneous`, `--delete-extraneous`, `--review` CLI flags — a live
  [Spectre.Console](https://spectreconsole.net/) status table at a terminal, plain per-step lines
  when stdout is redirected, and JSON events POSTed to a listener when `--report-to` names one.
- An interactive front door: with no arguments the runner offers its modes as a menu, and after a
  run it offers each failure for inspection — message, stack trace, failing step, artifact paths.
- A screenshot viewer web page (static after every run, or served live via `--review`): a
  pannable/zoomable graph of the journey forest with thumbnails, failure badges, and extraneous
  highlighting, plus interactive failure triage (Accept/Reject) and extraneous cleanup.

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

The framework is consumed by a console executable that supplies its own journeys, platform fixtures,
and per-app mock state.

### 1. Reference the project

The framework currently ships as source — reference it via `<ProjectReference>` to a sibling
checkout. NuGet packaging is on the roadmap.

```xml
<ProjectReference Include="../../../MobileJourneys/MobileJourneys/MobileJourneys.csproj" />
```

### 2. Implement `IJourneyEnvironment`

Define a record that captures your app's per-journey state (the fields each journey can override).
`BackendUrl` is the one value handed to the app at launch — as a process environment variable on
iOS and an intent string extra on Android — under the name `FrameworkConfig.Backend` names.
Everything else reaches the app from the backend, so it needs no launch plumbing. `ForFixture` lets
you specialize the environment per-platform fixture (e.g., pin language to match the fixture's
theme).

```csharp
public record MyAppEnvironment : IJourneyEnvironment
{
    public bool LoggedIn { get; init; } = true;
    public bool ShowOnboarding { get; init; }
    public string Name { get; init; } = "Default";

    /// Filled in by your IJourneyBackend.PrepareFor once it knows the address it is serving on.
    public string ServiceUrl { get; init; } = "";

    public string BackendUrl => ServiceUrl;

    public IJourneyEnvironment ForFixture(PlatformConfig config) => this;
}
```

Declare the backend and the name its address arrives under together, so a suite cannot launch the
app under a name it does not read:

```csharp
new FrameworkConfig(...)
{
    Backend = new(
        (config, deviceId) => new MyAppBackend(config, deviceId),
        MyApp.LaunchVariables.BackendUrl),
}
```

The name is owned by the consumer rather than the framework, because the app reading it cannot
reference this assembly.

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

Journeys are authored with a **factory DSL** imported via `using static`, so a flow reads as bare
calls instead of a wall of `new`. `MobileJourneys.Dsl` supplies a thin factory for every built-in
action, expectation, and tree type (`Tap`, `Found`, `Step`, `Branch`, `Tree`, …); because each
returns the concrete type, nested calls compose without bracketing. `Step`'s expectations are
`params`, so a step with no mask elements needs no `[ ]` around them.

```csharp
using static MobileJourneys.Dsl;

public static class Journeys
{
    public static readonly JourneyDefinition Login = new(
        new MyAppEnvironment { LoggedIn = false },
        [Found("LoginButton")],
        [
            Step(Tap("LoginButton"), Found("EmailField")),
            Step(TypeText("EmailField", "test@example.com")),
            Step(TypeText("PasswordField", "hunter2")),
            Step(Tap("SubmitButton"), Found("HomeScreen")),
        ]);

    public static IEnumerable<JourneyDefinition> All => [Login /*, …*/];
}
```

Journeys that share step prefixes can be defined as a `JourneyTree` instead: the root holds the
environment and initial-screen expectations, interior `Branch` nodes hold steps shared by several
journeys, and terminal (childless) `Branch` nodes hold the steps unique to one journey. `Flatten()`
yields one ordinary `JourneyDefinition` per terminal node — the runner and selection flags are
unaware of trees — but each shared step's screenshot is stored once, in a folder hierarchy mirroring
the tree, instead of once per journey. Step numbers are the depth along the path, so a shared step
keeps one stable filename.

`Branch` has two shapes. A terminal node takes its steps as `params` —
`Branch("About", Step(…), Step(…))` — so a leaf reads without brackets. An interior node takes an
explicit steps array followed by its children array — `Branch("Menu", [Step(…)], [child, …])`.
(Keeping the children a plain array rather than `params` is what lets the two overloads coexist
unambiguously.)

```csharp
using static MobileJourneys.Dsl;

public static class Journeys
{
    private static readonly JourneyTree Home = Tree(
        new MyAppEnvironment { LoggedIn = true },
        [Found("HomeScreen")],
        [],
        [
            Branch(
                "Menu",
                [Step(Tap("MenuButton"), Found("AboutItem"))],
                [
                    Branch("About", Step(Tap("AboutItem"), Found("AboutPage"))),
                    Branch("Settings", Step(Tap("SettingsItem"), Found("SettingsPage"))),
                ]),
        ]);

    public static IEnumerable<JourneyDefinition> All => Home.Flatten();
}
```

Like `JourneyDefinition`, a tree's name auto-populates from the field name via `[CallerMemberName]`,
so trees must be declared as named fields; branch names are explicit (they name the screenshot
folder). Journey names must be unique across the whole suite — `FrameworkConfig` validates this at
construction.

Two shapes are rejected at construction, because each is a mistake rather than a choice: a `Branch`
with neither steps nor children, and siblings sharing a name. The first is worth explaining — a
childless branch with no steps re-runs exactly the path its siblings already traverse, asserts
nothing they do not, and produces no screenshot of its own, so it costs a full journey execution per
fixture and buys nothing; give it steps to make it a journey, or children to make it a shared
prefix. (A tree _root_ with no steps is fine: its initial screenshot is the whole test.)

### 5. Wire up `Program.Main`

```csharp
internal static class Program
{
    public static Task<int> Main(string[] args) =>
        SuiteRunner.RunAsync(
            new FrameworkConfig("MyApp UI Tests", MyAppPlatforms.All, [.. Journeys.All]),
            args
        );
}
```

`SuiteRunner.RunAsync` owns everything else: parsing the flags (or offering the menu when there are
none), selecting journeys, starting Appium, bringing every fixture up before the first journey runs,
running the fixtures concurrently, refreshing the static viewer page, and returning the exit code (0
all passed, 1 any failure, 2 a bad command line).

Fixtures are brought up first so a device that cannot host the suite is known before any journey
starts. An Android app installed without embedded assemblies crashes at launch, and the runner
offers — at a terminal — to rebuild the consumer project with `-p:EmbedAssemblies=true` and try
again; with stdout redirected it prints the same hint and abandons the fixture.

### 6. Required csproj bits

The consumer csproj must:

```xml
<PropertyGroup>
    <UseMaui>true</UseMaui> <!-- if your app is MAUI -->
    <OutputType>Exe</OutputType>
    <!-- Not a test project: keeps the slow, simulator-dependent journeys out of
         solution-wide `dotnet test` runs. Run them with `dotnet run` instead. -->
    <IsTestProject>false</IsTestProject>
    <IsTestingPlatformApplication>false</IsTestingPlatformApplication>
</PropertyGroup>
<ItemGroup>
    <!-- Required: lets TestAssembly resolve ProjectDir / RepoRoot. -->
    <AssemblyMetadata Include="ProjectDir" Value="$(MSBuildProjectDirectory)" />
    <AssemblyMetadata Include="RepoRoot" Value="$(RepoRoot)" />
</ItemGroup>
```

## Running

```bash
# No arguments: choose a mode from the menu (needs a terminal)
dotnet run --project test/MyApp.UITests

# All journeys, all platforms. --run is required — no other flag starts a run.
dotnet run --project test/MyApp.UITests -- --run

# Named journeys, on every platform. --journey matches a whole journey name
# case-insensitively, and several are ORed.
dotnet run --project test/MyApp.UITests -- --run --journey Login --journey Logout

# A single journey on the iOS light fixture. Filters are ANDed with each other and
# with --journey, and each is a case-insensitive substring of "{platform}.{journey}".
dotnet run --project test/MyApp.UITests -- --run --journey Login --filter "iPhone 17 Pro"

# Re-run only journeys with failure artifacts on disk
dotnet run --project test/MyApp.UITests -- --run --rerun

# Report progress as JSON events instead of to the console. Machine-facing: the review
# server passes its own endpoint here when it launches a rerun.
dotnet run --project test/MyApp.UITests -- --run --report-to http://localhost:8017/api/run-events?job=<id>

# Maintenance: detect / clean up orphaned baselines after journey renames
dotnet run --project test/MyApp.UITests -- --list-extraneous
dotnet run --project test/MyApp.UITests -- --delete-extraneous

# Serve the screenshot viewer with review actions enabled
dotnet run --project test/MyApp.UITests -- --review
```

## Screenshots

Baselines live under
`<consumer-project>/Screenshots/<PlatformConfig.DisplayName>/<container>/<NN> <StepLabel>.png`,
where the container is the journey's own folder for flat `JourneyDefinition`s, or the tree node's
nested folder path (e.g. `Home/Menu/About/`) for tree-defined journeys — shared steps are stored
once. The first run for a new step produces the baseline; subsequent runs compare (on shared steps,
whichever journey runs first writes the baseline and the rest compare mask-aware). Failure artifacts
(the actual capture for a mismatch, the diff visualization, FAIL screenshots from exceptions, and
crash logs) land alongside the baseline, stamped with the producing journey's name so runs through a
shared node can't clobber each other's evidence, and are auto-cleaned when the step next passes.
Filename layout is owned by the storage backend; with the default `FilesystemScreenshotStorage` they
appear as `<step> [<journey>].new.png`, `<step> [<journey>]_diff_<pct>%.png`,
`<step> [<journey>]_FAIL_<reason>.png`, and `<step> [<journey>].CRASH.txt`.

A FAIL screenshot records its cause twice, for two different readers. The filename keeps a
sanitized, 80-char reason so a directory listing still tells you what happened, and the PNG's text
metadata carries the untruncated original — exception type, message, and stack trace — which is what
the viewer displays. The metadata is the only source the viewer reads; an artifact written by an
older version simply shows no details until the journey is rerun.

It is captured the instant the step fails, before the app-state query and device crash log run.
Those diagnostics take seconds, which is long enough for a screen that was merely slow to finish
rendering — and evidence showing a perfectly good screen makes a timing failure look inexplicable.

`RelaunchApp` waits for the app to reach the foreground with an accessibility tree that has stopped
changing (capped at 30s) before returning. A cold start is therefore absorbed by the launch rather
than spending the first expectation's timeout budget, which is what otherwise turns a slow launch
into an "element not found" on the very first step.

## Screenshot viewer

Every test run writes a self-contained viewer to `Screenshots/viewer/` (`index.html` +
`manifest.js`). Opened directly from disk it gives a read-only, pannable/zoomable view of the whole
journey forest, drawn as **nested boxes**: a branch box contains its children, each level a shade
lighter than its parent, so the picture is the trie rather than a diagram of it. Thumbnails sit in
the box that owns them, journey chips mark the nodes where a journey ends (only when the node isn't
already named after it), a red outline marks a box with a failing step of its own — deeper failures
show as "N ✕ below" — orange marks extraneous files, and a blue ring is the current selection.

Run with `--review` to serve the same page from a local HTTP port (it prints the URL to open in a
browser); the manifest is rebuilt on every load and triage is enabled **inline in the tree**:
`j`/`k` walk the failures, expanding the selected one in place into a panel with the
baseline/new/diff panes and its actions, and `z` blows it up to fill the window with zoom and pan on
each image. Actions are Accept (promote its `.new` capture to the baseline; note the promoted PNG
carries no embedded mask metadata until the next run regenerates it), Discard (delete that journey's
artifacts for the step, leaving the baseline alone), deleting extraneous files individually or all
at once, and rerunning a journey — on the current fixture, all fixtures, or only the fixtures where
it currently fails. A rerun covers the selected journey only — the scopes differ in which fixtures
it runs on, not in how many journeys run. It shells out to `dotnet run … -- --run --journey <name>`,
narrowed by `--filter` for the current fixture or by `--rerun` for the fixtures that currently fail,
and reloads the view when it finishes. Only one rerun runs at a time, since concurrent runs would
fight over the simulators. Press `u` to reload from disk by hand if a run's completion is ever
missed.

### How a rerun reports back

The rerun stays a separate process — a crash there must not take the review server with it — and
tells the server what it is doing over HTTP. The server passes its own `api/run-events` endpoint to
the child as `--report-to`, the child's `WebReporter` POSTs one JSON object per event (run started
with each fixture's journey and step counts, fixture ready, fixture skipped with its reason, step
completed, journey completed, run finished with the exit code), and the server relays each to the
page over Server-Sent Events on `api/events`. A step event names the fixture, the screenshot
container and the numbered step, which is what lets the page recolour that one screenshot the
moment it is known: green as it passes, red the instant it fails, yellow for everything still to
come. A post that fails is swallowed by the child — the listener is a spectator and must never fail
the run it is watching.

Posting is synchronous and the server records an event before answering, so events reach the page
in the order the run produced them. The stream replays from the first event on every connection,
so a reloaded or reconnected page rebuilds the whole picture rather than resuming mid-run — nothing
on the page is accumulated by counting.

The child's console output is still captured, because a child that fails to _build_ posts no events
at all and its stdout is then the only evidence. It is held back while the run is live and handed
over with the terminal `process-exited` event when the exit code is non-zero, where the page appends
it under the event log.

Resolving the selected failure — a rerun passing, or Accept/Discard — deliberately does not advance
the selection. Snapping to whatever failure fell into the vacated slot is disorienting and hides the
fact that anything succeeded. Instead the resolved node is held in place with a green outline and a
✓ note, the status line turns green with the remaining count, and the log box's header goes green;
you move on with `j`/`k`, which clears the green state. The log box persists as the record of the
run and is dismissed with `Esc` or its ✕.

Press `?` in the page for the keymap. Highlights: `1`–`4` and `[`/`]` switch fixture, `j`/`k` step
through failures (centering each), `z` zooms the selected one, `b`/`n`/`d`/`space` rotate the
leading image pane through baseline/new/diff, `f` fits the tree, `u` reloads from disk, `a` accepts,
`x` discards, and rerun is a two-key chord (`r` then `r`/`a`/`f`) so an expensive run can't fire on
a single keypress.

Anything that has to stay legible while zoomed — emphasis outlines, the selection ring, node titles
— is sized in units divided by the zoom scale (`--inv`), so outlines keep a constant on-screen
thickness and titles stay within a readable min/max no matter how far in or out you are. Dragging
anywhere pans, including on top of a screenshot; a press that doesn't move is still a click.

Image URLs carry a `?v=` token (the file's last-write time) and the server serves those responses
`immutable`, so switching fixtures back and forth reloads nothing — an unchanged file keeps its URL
and comes from the browser cache with no request. A file that changes gets a new token, hence a new
URL, and is refetched; a stale file can never share a URL with its replacement. The page and
`manifest.js` are served `no-store` so the version map itself is always current. (The `shots/` vs
`../` URL form is fixed by how the page was loaded — http vs `file://` — not by live connectivity,
so a momentary server drop never rewrites image URLs or breaks the cached images.)

The connected badge is kept honest by a lightweight `api/ping` every few seconds rather than a
one-time check, so stopping or restarting the server flips it (and disables/re-enables the actions)
within a poll — a restarted server reconnects on its own. A failed action request flips it
immediately without waiting for the next poll.

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

Give your app-specific actions and expectations factory helpers of their own — a static `AppDsl`
class mirroring `MobileJourneys.Dsl`, one thin method per type — and import it with a second
`using static` next to the framework's. Journeys then read uniformly whether a step uses a built-in
action or one of yours; nothing in the DSL is special to the framework's own types, so there is no
reason for a call site to fall back to `new` for an app-specific one.

```csharp
namespace MyApp.UITests;

public static class AppDsl
{
    public static TapLocalizedAlert TapLocalizedAlert(string english, string japanese) =>
        new(english, japanese);
    // … one factory per app-specific action / expectation
}
```

```csharp
using static MobileJourneys.Dsl;   // Tap, Found, Step, Branch, Tree, …
using static MyApp.UITests.AppDsl; // your app's actions
```

## Worked example

The Beerbox app (`~/Projects/beerbox`) is the original consumer this framework was extracted from —
see its `Beerbox.Journeys` project for a complete production example: `BeerboxPlatforms.cs`,
`MockEnvironment.cs`, its app-specific actions and their `AppDsl`, and its journeys authored with
the DSL.

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

### Versioning & releases

The library version is derived from git tags by [MinVer](https://github.com/adamralph/minver) at
build time — there is no `<Version>` in any csproj. Tagging is **automated**: the `release` job in
[.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every push to `main` (after tests pass)
and uses [`mathieudutour/github-tag-action`](https://github.com/mathieudutour/github-tag-action) to
read the [Conventional Commits](https://www.conventionalcommits.org/) since the last tag, compute
the next SemVer, push the tag, and publish a GitHub Release with the changelog. MinVer stamps that
tag on the next build, so **no manual `git tag` is needed** — just merge with well-formed commit
messages.

| Commit type(s) since last tag                              | Result     |
| ---------------------------------------------------------- | ---------- |
| `feat:`                                                    | minor      |
| `fix:` / `perf:`                                           | patch      |
| `!` or `BREAKING CHANGE:`                                  | major      |
| only `docs`/`chore`/`ci`/`refactor`/`test`/`style`/`build` | no release |

To cut a specific version by hand, tag and push directly (`git tag 1.2.3 && git push origin 1.2.3`);
tags are plain SemVer, no `v` prefix.

## Status

- v0: ProjectReference-only consumption from sibling repos.
- v1 (planned): NuGet package on a public feed.
