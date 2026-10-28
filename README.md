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
  entry point, with `--run`, `--journey`, `--filter`, `--rerun`, `--report-to`, `--list-extraneous`,
  `--delete-extraneous`, `--review` CLI flags — a live
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

A simulator or emulator need not be running before a run: Appium boots a shut-down iOS simulator,
and the framework starts each Android emulator from its AVD. Both have to exist. On macOS:

```bash
xcrun simctl list devices       # the simulators that exist
emulator -list-avds             # the AVDs that exist
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
`BackendUrl` is the one value handed to the app at launch — as a process environment variable on iOS
and an intent string extra on Android — under the name `FrameworkConfig.Backend` names. Everything
else reaches the app from the backend, so it needs no launch plumbing. The app's locale comes from the
fixture's `AppLocale` instead, which the framework applies each time `RelaunchApp` launches the app.

```csharp
public record MyAppEnvironment : IJourneyEnvironment
{
    public bool LoggedIn { get; init; } = true;
    public bool ShowOnboarding { get; init; }
    public string Name { get; init; } = "Default";

    /// Filled in by your IJourneyBackend.PrepareFor once it knows the address it is serving on.
    public string ServiceUrl { get; init; } = "";

    public string BackendUrl => ServiceUrl;
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
            AppLocale: CultureInfo.GetCultureInfo("en-US"),
            AppIdentifier: AppId,
            AppBinaryPath: Path.Combine(TestAssembly.RepoRoot, "src/MyApp.app"),
            NotificationBannerTop: 176,
            NotificationBannerBottom: 412,
            ColorTolerance: 9,
            MaxDiffPixelPercentage: 0),
        new AndroidPlatformConfig(
            PlatformVersion: "15",
            DeviceName: "Pixel 8",
            AvdName: "Pixel_8_API35",
            IsLightTheme: true,
            AppLocale: CultureInfo.GetCultureInfo("en-US"),
            AppIdentifier: AppId,
            AppBinaryPath: Path.Combine(TestAssembly.RepoRoot, "src/MyApp-Signed.apk"),
            MainActivity: null,
            NotificationBannerTop: 153,
            NotificationBannerBottom: 385,
            ColorTolerance: 180,
            MaxDiffPixelPercentage: 0.005),
    ];
}
```

`AppLocale` must name a region (`en-US`, not `en`), so the date and number formats the app shows are fixed
by the fixture. Android fixtures need Android 13 or later, the first with per-app locales.

A step passes when no more than `MaxDiffPixelPercentage` percent of its pixels differ from the
baseline by more than `ColorTolerance`, the sum of the red, green and blue differences. Masked
regions are left out of the comparison. A percentage of 0 fails on any pixel past the tolerance;
Android decodes images slightly differently from one run to the next, so its fixtures usually need a
larger tolerance and a small percentage.

A fixture runs its journeys on one device unless told otherwise. Set `Instances` to run it on
several at once, with the journeys shared between them longest-first:

```csharp
new IosPlatformConfig(...) { Instances = 3 },
```

On iOS, worker 1 is the simulator the config names and the extras are simulators named
`<DeviceName> · worker <n>`, created from the base device's type on first use and kept for later
runs. On Android every instance of a shared AVD runs read-only, since the emulator allows no
writable one beside them. All are left running after a run, like the base device.

A run keeps the Android emulators it finds running while they are fresh, and restarts all of an
AVD's instances once any of them has been up for more than an hour: each is killed and a new one
booted from the AVD's quick-boot snapshot, which takes seconds. An emulator gets slower the longer
it runs — one left up for three days launched apps six times slower than a fresh boot of the same
AVD, while ones a few hours old were as quick — and that slowness reaches every step of every
journey. Killing rather than stopping gracefully keeps a writable instance from saving its worn
state into the snapshot the next boot loads.

Android emulators run without a window (`-no-window`), and a run restarts one it finds running with
a window. macOS drops an emulator whose window is covered to background priority, where its virtual
CPUs share the Mac's few efficiency cores, and the app then takes longer to start than a journey
waits — while the Mac as a whole looks far from busy.

An emulator the run starts that exits before all of a fixture's instances are up is replaced once,
and the run reports the retry; if the replacement exits too, the fixture is abandoned. Each emulator
the run starts writes its output to a file of its own in `MobileJourneys/emulators/` under the
system's temporary directory, not in the run's folder; the [run log](#the-run-log) names each one's
file.

A device that comes up but cannot host the app drops out, and the fixture's other devices take its
journeys. A device cannot host the app when no session opens on it, when the app crashes on startup
twice, or when its backend fails to start; one also drops out when its session dies, or the app
stops responding on it, more often than its retry budget allows. Once its devices are up, the
fixture is abandoned only when none of them can host the app.

### 4. Write journeys

Journeys are authored with a **factory DSL** imported via `using static`, so a flow reads as bare
calls instead of a wall of `new`. `MobileJourneys.Dsl` supplies a thin factory for every built-in
action, expectation, and tree type (`Tap`, `Found`, `Step`, `Branch`, `Tree`, …); because each
returns the concrete type, nested calls compose without bracketing. `Step`'s expectations are
`params`, so a step with no mask elements needs no `[ ]` around them.

`Step` has four shapes: `Step(action)`; `Step(action, expect…)`;
`Step(action, [expect], [maskIds])`, whose mask elements — the AutomationIds of elements left out of
the screenshot comparison — put the expectations back in brackets, since they are no longer the last
argument; and the same with a trailing `prefetchMasks` bool, which looks the mask elements up before
the action runs, for an action that hides them (an alert covering them, say).
`Tree(environment, [initialExpect], [steps], [children])` builds a tree's root, with an optional
`[initialMaskElements]` after the children for its initial screenshot.

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

A device's automation process can also die part-way through a fixture, while the app and the Appium
server both stay up. Every command after that fails identically, so the worker replaces the session
and gives the interrupted journey the run it never got, keeping the rest of its journeys. Three lost
sessions and the worker is lost instead — a device needing more than that is reporting its own
condition, not the app's — and the fixture's other workers take the journeys it would have run. Only
when a fixture's last worker is lost are its remaining journeys counted as not run.

A loaded host freezes an app the same way. Android then puts its own "isn't responding" dialog over
whatever was on screen, and because that dialog answers to the alert endpoint like any other, an
expectation waiting for an alert accepts it and the journey fails on a screenshot of the system's
verdict rather than of the app. So a failed journey is asked, before it is recorded, whether that
dialog is up: if it is, it is cleared by closing the app and the journey runs again, exactly as a
lost session does. Three journeys frozen and the worker is lost instead, with the run saying that
the machine is most likely driving more devices than it can — which is the real finding, and one no
journey's failure would have shown.

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

# The flags, from the runner itself
dotnet run --project test/MyApp.UITests -- --help
```

`--run` is what starts a run: a bare `--journey`, `--filter` or `--rerun` is rejected rather than
taken as one, so a long run is never started by accident. `--journey`, `--filter` and `--rerun` then
narrow it independently. Each of `--journey` and `--filter` takes several values at once
(`--filter A B`) with the same meaning as repeating the flag. Name a single journey with
`--journey`: a filter is a substring, so `--filter Loading` also selects `LoadingNoConnectivity`.

With no arguments at all the runner offers a menu: run every journey, pick journeys to run (with
optional filters after), rerun the journeys that have failure artifacts, review screenshots in the
browser, list or delete the screenshots no journey references, or quit. With no terminal to show it
in, no arguments is a bad command line.

At a terminal the runner draws a live table, one row per fixture, and offers each failure for
inspection when the run is over. When stdout is redirected, or `NO_COLOR` is set, it writes one
plain line per step instead and prints the failures in full. That plain output is what CI captures
and what the viewer shows for a failed rerun, so it carries no ANSI escapes and no hard wrapping.

### The run log

Start with the run log when a run needs explaining. Every run writes `run.log` to a folder of its
own, `MobileJourneys/runs/<date>-<time>-<process id>/` under the system's temporary directory, and
the runner prints its path at the start and again above the verdict. It holds the run's whole
timeline, each entry stamped with the time and the run, fixture or worker it concerns: what was
selected; each app binary and when it was last written; every emulator kept or stopped, and every
one started with the file its output goes to; each device id and Appium session; every step and
journey outcome; and every retry, lost worker and abandoned fixture, with the whole exception or
crash log the console leaves out. A step's screenshots and crash log are written beside its
baseline. The Appium server's own log is `appium.log` beside the run log. macOS's nightly cleanup
deletes files in the temporary directory after three days.

## Screenshots

Baselines live under
`<consumer-project>/Screenshots/<PlatformConfig.DeviceName>/<container>/<NN> <StepLabel>.png`,
where the container is the journey's own folder for flat `JourneyDefinition`s, or the tree node's
nested folder path (e.g. `Home/Menu/About/`) for tree-defined journeys — shared steps are stored
once. A step with no baseline yet fails, and its capture is kept as the same `.new` artifact a
mismatch leaves; it becomes the baseline only once it is accepted in the viewer, so no screenshot is
compared against until someone has looked at it. Later runs compare against it (on shared steps,
mask-aware, so every journey through the step is held to the one baseline). Failure artifacts
(the actual capture for a mismatch, the diff visualization, FAIL screenshots from exceptions, and
crash logs) land alongside the baseline, stamped with the producing journey's name so runs through a
shared node can't clobber each other's evidence, and are auto-cleaned when the step next passes.
Filename layout is owned by the storage backend; with the default `FilesystemScreenshotStorage` they
appear as `<step> [<journey>].new.png`, `<step> [<journey>]_diff_<pct>%.png`,
`<step> [<journey>]_FAIL_<reason>.png`, `<step> [<journey>].CRASH.txt`, and
`<step> [<journey>].ERROR.txt`.

A FAIL screenshot records its cause twice, for two different readers. The filename keeps a
sanitized, 80-char reason so a directory listing still tells you what happened, and the PNG's text
metadata carries the untruncated original — exception type, message, and stack trace — which is what
the viewer displays. A step whose failure also took the screenshot down — a dead session answers a
capture request the same way it answers everything else — leaves a zero-byte FAIL marker with no
metadata to carry, so the same text is written beside it as `<step> [<journey>].ERROR.txt` and the
viewer reads whichever of the two exists. An artifact written by an older version shows no details
until the journey is rerun.

It is captured the instant the step fails, before the app-state query and device crash log run.
Those diagnostics take seconds, which is long enough for a screen that was merely slow to finish
rendering — and evidence showing a perfectly good screen makes a timing failure look inexplicable.

`RelaunchApp` waits for the app to reach the foreground with an accessibility tree that has stopped
changing (within the wait budget) before returning. A cold start is therefore absorbed by the launch
rather than spending the first expectation's budget, which is what otherwise turns a slow launch
into an "element not found" on the very first step.

## Waits and the timing report

Every wait for the app — an element or alert to appear, a launch to settle, the screen to match its
baseline, a notification banner to arrive — has the same budget: the fixture's `WaitBudget`, set on
its config like `Instances`. A wait returns the moment its condition holds, so a generous budget
costs a passing run nothing; only a failing step pays it. That is also why a step that throws ends
its journey — the steps after it would each spend the whole budget on a screen that is not coming —
while a screenshot that merely differs lets the journey carry on, since the app is still on the
screen the next step expects. Probes that usually find nothing, such as `DismissAlert` giving an
alert a moment to show up, keep their own short timeouts, because they pay them in full every time.

Every session records how long each lookup round trip to its device took and how long each wait
took, and the end of a run prints them per fixture: how many journeys it ran and how long it took,
the median and slowest lookup, the average and longest wait that succeeded (with what it was for and
how much of the budget it used), and how many waits ran out. The lookup figures are the device's own
speed, independent of what the app is doing, so a device that has slowed down over hours of use
shows there before it shows as timeouts. The run time is the whole run, devices starting included,
which is the number to weigh a fixture's `Instances` against.

## Screenshot viewer

Every test run writes a self-contained viewer to `Screenshots/viewer/` (`index.html` +
`manifest.js`). Opened directly from disk it gives a read-only, pannable/zoomable view of the whole
journey forest, drawn as **nested boxes**: a branch box contains its children, each level a shade
lighter than its parent, so the picture is the trie rather than a diagram of it. Thumbnails sit in
the box that owns them, journey chips mark the nodes where a journey ends (only when the node isn't
already named after it), a red outline marks a box with a failing step of its own — deeper failures
show as "N ✕ below" — violet marks new screenshots (steps with no baseline yet, shown by their
capture), orange marks extraneous files, and a blue ring is the current selection.

Run with `--review` to serve the same page from a local HTTP port (it prints the URL to open in a
browser); the manifest is rebuilt on every load and triage is enabled: `j`/`k` walk the failures,
and clicking a screenshot, or `z` for the selected failure, opens the zoom view, which fills the
window with zoom and pan on each image — the baseline/new/diff panes for a failure, the baseline
alone for a step that passed. Actions are Accept (promote its `.new` capture to the baseline),
Discard (delete that journey's artifacts for the step, leaving the baseline alone), accepting every
new screenshot on the current fixture at once, deleting extraneous files individually or all at
once, and rerunning the journey the zoom view shows — on the current fixture, all fixtures, or only
the fixtures where it currently fails, each with or without rebuilding the app first. A step several
journeys share asks which of them to rerun, or all. The scopes differ in which fixtures the rerun
runs on, not in how many journeys run. It shells out to `dotnet run … -- --run --journey <names>`,
narrowed by `--filter` for the current fixture or by `--rerun` for the fixtures that currently fail,
with `-p:EmbedAssemblies=true` to rebuild, and reloads the view as each step lands. Only one rerun
runs at a time, since concurrent runs would fight over the simulators. Press `u` to reload from disk
by hand if a run's completion is ever missed.

### How a rerun reports back

The rerun stays a separate process — a crash there must not take the review server with it — and
tells the server what it is doing over HTTP. The server passes its own `api/run-events` endpoint to
the child as `--report-to`, the child's `WebReporter` POSTs one JSON object per event (run started
with each fixture's journey and step counts, fixture ready, fixture skipped with its reason, step
completed, journey completed, run finished with the exit code), and the server relays each to the
page over Server-Sent Events on `api/events`. A step event names the fixture, the screenshot
container and the numbered step, which is what lets the page recolour that one screenshot the moment
it is known: green as it passes, red the instant it fails, yellow for everything still to come. A
post that fails is swallowed by the child — the listener is a spectator and must never fail the run
it is watching.

Posting is synchronous and the server records an event before answering, so events reach the page in
the order the run produced them. The stream replays from the first event on every connection, so a
reloaded or reconnected page rebuilds the whole picture rather than resuming mid-run — nothing on
the page is accumulated by counting.

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
through failures (centering each), `/` finds a journey or branch by name, `z` zooms the selected
failure or what `/` found, `←`/`→` step through the journey (keeping to the journey the first press
started from), `space` rotates the leading image pane through baseline/new/diff, `d` blinks the
differing pixels red on every pane, `D` zooms 300% on the first of them (and back out), scrolling
and dragging zoom and pan every pane together and `0` resets them, `f` fits the tree, `u` reloads
from disk, `a` accepts, `x` discards, and rerun is a two-key chord (`r` then `r`/`a`/`f`, or
`R`/`A`/`F` to rebuild the app first) so an expensive run can't fire on a single keypress.

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
`Verify(TestDriver)`). Override the `Name` property if you want a wrapper class to share its
parent's baseline filename. This one picks the label for the language of the fixture's `AppLocale`,
which the framework launches the app in.

```csharp
public sealed record TapLocalizedAlert(string English, string Japanese) : JourneyAction(English)
{
    public override string Name => nameof(TapAlertButton); // baseline says "TapAlertButton …"
    public override void Execute(TestDriver driver) =>
        new TapAlertButton(driver.Config.AppLocale.TwoLetterISOLanguageName == "ja" ? Japanese : English)
            .Execute(driver);
}
```

An action that reads the journey's environment gets it from `CurrentJourneyEnv`, which `RelaunchApp`
updates, and which an action that changes the app's state without relaunching updates with
`AdoptEnvironment`.

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
