using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using MobileJourneys.Framework;

namespace MobileJourneys.Viewer;

/// <summary>
/// The screenshot viewer: a self-contained web page showing the journey tree with every
/// baseline as a pannable/zoomable graph, plus a review mode for failure triage and
/// extraneous-file cleanup. <see cref="WriteStaticAssets"/> drops the page and its data
/// manifest under <c>Screenshots/viewer/</c> for read-only browsing straight off the
/// filesystem; <see cref="RunReviewServer"/> serves the same page from a local HTTP
/// server, which additionally enables the Accept/Discard/Delete actions and re-running a
/// journey without leaving the page.
/// </summary>
public static class ScreenshotViewer
{
	private const int FirstPort = 8017;

	/// <summary>Most recent lines of a rerun's console output kept for the page to display.</summary>
	private const int MaxJobLines = 400;

	private static readonly JsonSerializerOptions RequestOptions = new() { PropertyNameCaseInsensitive = true };

	private static readonly JsonSerializerOptions ResponseOptions = new();

	/// <summary>Guards <see cref="jobs"/> and the single-rerun-at-a-time rule.</summary>
	private static readonly Lock RerunGate = new();

	private static readonly Dictionary<string, RerunJob> jobs = [];

	/// <summary>
	/// Writes <c>viewer/index.html</c> and <c>viewer/manifest.js</c> under the screenshots root,
	/// reflecting the current on-disk state. No-op when the configured storage is not
	/// filesystem-backed.
	/// </summary>
	/// <param name="config">Framework configuration providing the journeys, platforms, and storage.</param>
	public static void WriteStaticAssets(FrameworkConfig config)
	{
		if (config.Storage is not FilesystemScreenshotStorage storage)
		{
			return;
		}

		var viewerDir = Path.Combine(storage.RootDir, "viewer");
		_ = Directory.CreateDirectory(viewerDir);
		File.WriteAllText(Path.Combine(viewerDir, "index.html"), ReadIndexHtml());
		File.WriteAllText(Path.Combine(viewerDir, "manifest.js"), ViewerManifest.BuildJs(config));
	}

	/// <summary>
	/// Serves the viewer on a local HTTP port until Ctrl+C. The manifest is rebuilt on every
	/// page load, so the browser always sees the current on-disk state; the page's
	/// Accept/Discard/Delete/Rerun actions call back into this server.
	/// </summary>
	/// <param name="config">Framework configuration providing the journeys, platforms, and storage.</param>
	/// <returns>Process exit code.</returns>
	public static int RunReviewServer(FrameworkConfig config)
	{
		if (config.Storage is not FilesystemScreenshotStorage storage)
		{
			Console.Error.WriteLine("--review requires filesystem screenshot storage.");
			return 1;
		}

		using var listener = StartListener(out var url);
		Console.WriteLine();
		Console.WriteLine($"  Screenshot review server running — open {url} in your browser.");
		Console.WriteLine("  Press Ctrl+C to stop.");
		Console.WriteLine();
		using var stopped = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			stopped.Cancel();
			listener.Stop();
		};

		var indexHtml = ReadIndexHtml();
		while (!stopped.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = listener.GetContext();
			}
			catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
			{
				break;
			}

			_ = Task.Run(() =>
			{
				try
				{
					Handle(context, config, storage, indexHtml);
				}
				catch (Exception ex)
				{
					TryRespond(context, 500, "text/plain", Encoding.UTF8.GetBytes(ex.Message));
				}
			});
		}

		return 0;
	}

	private static HttpListener StartListener(out string url)
	{
		for (var port = FirstPort; ; port++)
		{
			var listener = new HttpListener();
			url = $"http://localhost:{port}/";
			listener.Prefixes.Add(url);
			try
			{
				listener.Start();
				return listener;
			}
			catch (HttpListenerException) when (port < FirstPort + 20)
			{
				listener.Close();
			}
		}
	}

	private static void Handle(
		HttpListenerContext context,
		FrameworkConfig config,
		FilesystemScreenshotStorage storage,
		string indexHtml
	)
	{
		var segments = context
			.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
			.Select(Uri.UnescapeDataString)
			.ToArray();

		switch (segments)
		{
			case [] or ["index.html"]:
				TryRespond(context, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(indexHtml), "no-store");
				return;
			case ["manifest.js"]:
				TryRespond(
					context,
					200,
					"text/javascript; charset=utf-8",
					Encoding.UTF8.GetBytes(ViewerManifest.BuildJs(config)),
					"no-store"
				);
				return;
			case ["shots", .. var shotPath] when shotPath.Length >= 2:
				ServeShot(context, storage, shotPath);
				return;
			case ["api", "ping"]:
				TryRespond(context, 200, "text/plain", Encoding.UTF8.GetBytes("ok"));
				return;
			case ["api", "rerun-status"]:
				ServeRerunStatus(context);
				return;
			case ["api", var action] when context.Request.HttpMethod == "POST":
				HandleApi(context, config, storage, action);
				return;
			default:
				TryRespond(context, 404, "text/plain", Encoding.UTF8.GetBytes("not found"));
				return;
		}
	}

	private static void ServeShot(HttpListenerContext context, FilesystemScreenshotStorage storage, string[] shotPath)
	{
		// Resolve to a full path and require it to stay inside the screenshots root. A per-segment ".."
		// check is not enough: a percent-encoded slash decodes into one segment (…/"..%2f.."→"../..")
		// after the URL is split, and a leading "%2f" decodes to an absolute segment that Path.Combine
		// would honor outright — both slip past a whole-segment comparison but not a containment check.
		var path = Path.GetFullPath(Path.Combine([storage.RootDir, .. shotPath]));
		var root = Path.GetFullPath(storage.RootDir);
		if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
		{
			TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("bad path"));
			return;
		}

		if (!File.Exists(path))
		{
			TryRespond(context, 404, "text/plain", Encoding.UTF8.GetBytes("not found"));
			return;
		}

		var contentType = Path.GetExtension(path).ToLowerInvariant() switch
		{
			".png" => "image/png",
			".txt" => "text/plain; charset=utf-8",
			_ => "application/octet-stream",
		};
		// A request carrying a ?v= token addresses one immutable version of the file — the page only
		// ever mints that URL for content it knows, and any change gives the file a new token and so
		// a new URL. Cache it hard. A bare URL (no token) must always revalidate.
		var cacheControl = string.IsNullOrEmpty(context.Request.QueryString["v"])
			? "no-cache"
			: "public, max-age=31536000, immutable";
		TryRespond(context, 200, contentType, File.ReadAllBytes(path), cacheControl);
	}

	private static void ServeRerunStatus(HttpListenerContext context)
	{
		var id = context.Request.QueryString["id"];
		RerunJob? job;
		lock (RerunGate)
		{
			job = id is not null && jobs.TryGetValue(id, out var found) ? found : null;
		}

		if (job is null)
		{
			TryRespond(context, 404, "text/plain", Encoding.UTF8.GetBytes("unknown job"));
			return;
		}

		TryRespond(
			context,
			200,
			"application/json",
			Encoding.UTF8.GetBytes(job.ToJson(context.Request.QueryString["config"]))
		);
	}

	private static void HandleApi(
		HttpListenerContext context,
		FrameworkConfig config,
		FilesystemScreenshotStorage storage,
		string action
	)
	{
		using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
		var body = reader.ReadToEnd();
		var expected = new ExpectedScreenshots(config.Journeys);

		switch (action)
		{
			case "accept" or "reject":
			{
				var request = JsonSerializer.Deserialize<StepRequest>(body, RequestOptions);
				var platform = FindPlatform(config, request?.Config);
				if (request is null || platform is null)
				{
					TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("bad request"));
					return;
				}

				if (!expected.IsExpectedStep(request.Container, request.Step, request.Journey))
				{
					TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("unknown step"));
					return;
				}

				var testStep = new TestStep(platform, request.Container, request.Step, request.Journey);
				if (action == "accept")
				{
					var newBytes = storage.ReadNewScreenshot(testStep);
					if (newBytes is null)
					{
						TryRespond(context, 409, "text/plain", Encoding.UTF8.GetBytes("no .new capture on disk"));
						return;
					}

					storage.WriteBaseline(testStep, newBytes);
				}

				storage.DeleteFailureArtifactsForStep(testStep);
				TryRespond(context, 200, "text/plain", Encoding.UTF8.GetBytes("ok"));
				return;
			}
			case "delete-extraneous":
			{
				var request = JsonSerializer.Deserialize<FileRequest>(body, RequestOptions);
				var platform = FindPlatform(config, request?.Config);
				if (request is null || platform is null)
				{
					TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("bad request"));
					return;
				}

				if (expected.IsExpected(request.Container, request.File))
				{
					TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("file is not extraneous"));
					return;
				}

				storage.DeleteStoredFile(platform, request.Container, request.File);
				storage.CleanupEmptyContainers(platform);
				TryRespond(context, 200, "text/plain", Encoding.UTF8.GetBytes("ok"));
				return;
			}
			case "delete-all-extraneous":
				_ = config.FindExtraneous(deleteExtraneous: true);
				TryRespond(context, 200, "text/plain", Encoding.UTF8.GetBytes("ok"));
				return;
			case "rerun":
				StartRerun(context, config, storage, JsonSerializer.Deserialize<RerunRequest>(body, RequestOptions));
				return;
			case "rerun-all":
				StartRerunAll(
					context,
					config,
					storage,
					JsonSerializer.Deserialize<RerunAllRequest>(body, RequestOptions)
				);
				return;
			default:
				TryRespond(context, 404, "text/plain", Encoding.UTF8.GetBytes("unknown action"));
				return;
		}
	}

	/// <summary>
	/// Reruns one journey on the requested platforms. Every scope reruns the same journey; they
	/// differ only in which fixtures it runs on — every platform ("all"), only the ones that
	/// currently have failures ("failed"), or the single originating one.
	/// </summary>
	/// <param name="context">The request to respond to.</param>
	/// <param name="config">Framework configuration providing the journeys and platforms.</param>
	/// <param name="storage">Storage consulted to find which platforms currently have failures.</param>
	/// <param name="request">The journey, scope, and originating platform.</param>
	private static void StartRerun(
		HttpListenerContext context,
		FrameworkConfig config,
		FilesystemScreenshotStorage storage,
		RerunRequest? request
	)
	{
		var journey = config.Journeys.FirstOrDefault(j => j.Name == request?.Journey);
		if (request is null || journey is null)
		{
			TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("unknown journey"));
			return;
		}

		List<PlatformConfig> platforms = request.Scope switch
		{
			"all" => [.. config.PlatformConfigs],
			"failed" => [.. config.PlatformConfigs.Where(p => storage.HasFailureArtifacts(p, journey))],
			_ => FindPlatform(config, request.Config) is { } single ? [single] : [],
		};

		if (platforms.Count == 0)
		{
			TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("no matching platforms"));
			return;
		}

		// --rerun re-derives the failing fixtures from the same predicate that built the platform list
		// above, so the run and the progress totals cannot disagree.
		List<string> scopeArgs = request.Scope switch
		{
			"all" => [],
			"failed" => ["--rerun"],
			_ => ["--filter", platforms[0].DisplayName],
		};

		var stepsPerJourney = journey.ExpectedStepLocations().Count();
		LaunchRerun(
			context,
			$"{journey.Name} on {string.Join(", ", platforms.Select(p => p.DisplayName))}",
			[],
			["--journey", journey.Name, .. scopeArgs],
			platforms.Count,
			platforms.Count * stepsPerJourney
		);
	}

	/// <summary>
	/// Reruns the whole suite. Two orthogonal flags: <see cref="RerunAllRequest.Failed"/> passes
	/// <c>--rerun</c> to run only the journeys that currently have failure artifacts (otherwise every
	/// journey on every platform); <see cref="RerunAllRequest.Embed"/> passes
	/// <c>-p:EmbedAssemblies=true</c> to rebuild and re-embed the app first (needed after app-code
	/// changes). The two combine.
	/// </summary>
	/// <param name="context">The request to respond to.</param>
	/// <param name="config">Framework configuration providing the journeys and platforms, for the progress totals.</param>
	/// <param name="storage">Storage consulted (when <c>Failed</c>) for which journeys still have failures.</param>
	/// <param name="request">Whether to limit to failing journeys and/or rebuild the app first.</param>
	private static void StartRerunAll(
		HttpListenerContext context,
		FrameworkConfig config,
		FilesystemScreenshotStorage storage,
		RerunAllRequest? request
	)
	{
		var failed = request?.Failed ?? false;
		var embed = request?.Embed ?? false;

		List<string> buildArgs = embed ? ["-p:EmbedAssemblies=true"] : [];
		List<string> runnerArgs = failed ? ["--rerun"] : [];

		int totalJourneys;
		int totalSteps;
		if (failed)
		{
			// Mirror the framework's own --rerun selection so the progress totals match what actually runs.
			var journeys = config
				.PlatformConfigs.SelectMany(p => config.Journeys.Where(j => storage.HasFailureArtifacts(p, j)))
				.ToList();
			totalJourneys = journeys.Count;
			totalSteps = journeys.Sum(j => j.ExpectedStepLocations().Count());
		}
		else
		{
			var platformCount = config.PlatformConfigs.Count;
			totalJourneys = config.Journeys.Count * platformCount;
			totalSteps = platformCount * config.Journeys.Sum(j => j.ExpectedStepLocations().Count());
		}

		var description = (failed ? "failed journeys" : "all journeys") + (embed ? " (rebuilding app)" : "");
		LaunchRerun(context, description, buildArgs, runnerArgs, totalJourneys, totalSteps);
	}

	/// <summary>
	/// Starts a <c>dotnet run</c> rerun with the given extra arguments appended to the shared ones,
	/// tracking it as the single active job. Only one rerun runs at a time — concurrent runs would
	/// fight over the simulators — so this answers 409 when one is already going.
	/// </summary>
	/// <param name="context">The request to answer with the new job's id, or an error.</param>
	/// <param name="description">Human-readable summary of what is being rerun, shown on the page.</param>
	/// <param name="buildArgs">MSBuild properties for <c>dotnet run</c> itself, before the <c>--</c>.</param>
	/// <param name="runnerArgs">Arguments for the runner, after the <c>--</c> — the journey selection, or <c>--rerun</c>.</param>
	/// <param name="totalJourneys">Total journey runs this rerun will perform (journeys × platforms), for the progress counter.</param>
	/// <param name="totalSteps">Total screenshot steps this rerun will perform, for the progress counter.</param>
	private static void LaunchRerun(
		HttpListenerContext context,
		string description,
		IEnumerable<string> buildArgs,
		IEnumerable<string> runnerArgs,
		int totalJourneys,
		int totalSteps
	)
	{
		var projectPath = Directory.GetFiles(TestAssembly.ProjectRootPath, "*.csproj").FirstOrDefault();
		if (projectPath is null)
		{
			TryRespond(context, 500, "text/plain", Encoding.UTF8.GetBytes("test project not found"));
			return;
		}

		// The running test process appends per-step/per-journey progress here (see ProgressLog); the
		// page polls rerun-status, which reads it back. A file side-channel rather than the console
		// output we already capture, because the page needs to identify the exact screenshot each
		// event belongs to, which structured lines give it and prose does not.
		var progressPath = Path.Combine(Path.GetTempPath(), $"mj-progress-{Guid.NewGuid():N}.tsv");
		var job = new RerunJob(description, progressPath, totalJourneys, totalSteps);
		lock (RerunGate)
		{
			if (jobs.Values.Any(j => j.Running))
			{
				TryRespond(context, 409, "text/plain", Encoding.UTF8.GetBytes("a rerun is already running"));
				return;
			}

			jobs[job.Id] = job;
		}

		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = TestAssembly.ProjectRootPath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		startInfo.Environment["MJ_PROGRESS_FILE"] = progressPath;
		startInfo.ArgumentList.Add("run");
		startInfo.ArgumentList.Add("--project");
		startInfo.ArgumentList.Add(projectPath);
		foreach (var arg in buildArgs)
		{
			startInfo.ArgumentList.Add(arg);
		}
		// Everything after this belongs to the runner, not to `dotnet run` itself.
		startInfo.ArgumentList.Add("--");
		startInfo.ArgumentList.Add("--run");
		foreach (var arg in runnerArgs)
		{
			startInfo.ArgumentList.Add(arg);
		}

		job.Append($"$ dotnet {string.Join(" ", startInfo.ArgumentList.Select(Quote))}");
		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
		process.OutputDataReceived += (_, e) => job.Append(e.Data);
		process.ErrorDataReceived += (_, e) => job.Append(e.Data);
		process.Exited += (_, _) =>
		{
			job.Complete(process.ExitCode);
			try
			{
				File.Delete(progressPath);
			}
			catch (IOException)
			{
				// Best-effort cleanup of the temp progress file; a leftover in TEMP is harmless.
			}
			process.Dispose();
		};

		try
		{
			_ = process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
		{
			job.Append($"failed to start dotnet: {ex.Message}");
			job.Complete(-1);
			process.Dispose();
		}

		TryRespond(context, 200, "application/json", Encoding.UTF8.GetBytes($$"""{"id":"{{job.Id}}"}"""));
	}

	private static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;

	private static PlatformConfig? FindPlatform(FrameworkConfig config, string? displayName) =>
		config.PlatformConfigs.FirstOrDefault(p => p.DisplayName == displayName);

	private static void TryRespond(
		HttpListenerContext context,
		int status,
		string contentType,
		byte[] body,
		string? cacheControl = null
	)
	{
		try
		{
			context.Response.StatusCode = status;
			context.Response.ContentType = contentType;
			if (cacheControl is not null)
			{
				context.Response.Headers["Cache-Control"] = cacheControl;
			}
			context.Response.ContentLength64 = body.Length;
			context.Response.OutputStream.Write(body);
			context.Response.Close();
		}
		catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
		{
			// The browser hung up mid-response (page reload, closed tab) — nothing to salvage.
		}
	}

	private static string ReadIndexHtml()
	{
		using var stream = typeof(ScreenshotViewer).Assembly.GetManifestResourceStream(
			$"{typeof(ScreenshotViewer).Namespace}.index.html"
		)!;
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return reader.ReadToEnd();
	}

	private sealed record StepRequest(string Config, string Container, string Step, string Journey);

	private sealed record FileRequest(string Config, string Container, string File);

	private sealed record RerunRequest(string Config, string Journey, string Scope);

	private sealed record RerunAllRequest(bool Failed, bool Embed);

	/// <summary>A running or finished <c>dotnet run</c> rerun, and the console output collected from it.</summary>
	/// <param name="description">Human-readable summary of what is being rerun, shown on the page.</param>
	/// <param name="progressPath">File the test process appends per-step/per-journey progress lines to.</param>
	/// <param name="totalJourneys">Total journey runs this rerun performs, for the progress counter.</param>
	/// <param name="totalSteps">Total screenshot steps this rerun performs, for the progress counter.</param>
	private sealed class RerunJob(string description, string progressPath, int totalJourneys, int totalSteps)
	{
		private readonly List<string> lines = [];
		private readonly object gate = new();

		/// <summary>Identifier the page polls status with.</summary>
		internal string Id { get; } = Guid.NewGuid().ToString("N");

		/// <summary><c>true</c> until the process exits.</summary>
		internal bool Running { get; private set; } = true;

		private int ExitCode { get; set; }

		/// <summary>
		/// Appends one console line, discarding the oldest once the cap is reached. The rerun's
		/// stdout is redirected, which makes the runner drop its colour codes, so the text is shown
		/// as-is.
		/// </summary>
		/// <param name="line">The line to append; <c>null</c> (the end-of-stream marker) and blank lines are ignored.</param>
		internal void Append(string? line)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				return;
			}

			lock (gate)
			{
				lines.Add(line.TrimEnd());
				if (lines.Count > MaxJobLines)
				{
					lines.RemoveRange(0, lines.Count - MaxJobLines);
				}
			}
		}

		/// <summary>Marks the job finished with the process's exit code.</summary>
		/// <param name="exitCode">The exit code <c>dotnet run</c> returned.</param>
		internal void Complete(int exitCode)
		{
			lock (gate)
			{
				ExitCode = exitCode;
				Running = false;
			}
		}

		/// <summary>Serializes the current status, output, and progress for the page.</summary>
		/// <param name="config">Platform whose per-screenshot progress the page wants; counts stay global.</param>
		internal string ToJson(string? config)
		{
			lock (gate)
			{
				return JsonSerializer.Serialize(
					new
					{
						id = Id,
						description,
						running = Running,
						exitCode = ExitCode,
						lines,
						progress = ReadProgress(config),
					},
					ResponseOptions
				);
			}
		}

		/// <summary>
		/// Reads the progress file the test process is appending to: overall journeys/steps completed
		/// (across all platforms, for the counter) plus a per-screenshot pass/fail map for the requested
		/// platform (for recoloring the graph). Missing/partly-written lines are ignored, so a torn read
		/// mid-append just yields slightly stale progress.
		/// </summary>
		/// <param name="config">Platform whose step results to include; <c>null</c> includes all.</param>
		private object ReadProgress(string? config)
		{
			var steps = new Dictionary<string, string>(StringComparer.Ordinal);
			var doneJourneys = 0;
			var doneSteps = 0;
			string text;
			try
			{
				using var stream = new FileStream(progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var reader = new StreamReader(stream, Encoding.UTF8);
				text = reader.ReadToEnd();
			}
			catch (IOException)
			{
				text = "";
			}

			foreach (var line in text.Split('\n'))
			{
				switch (line.Split('\t'))
				{
					case ["step", var cfg, var container, var step, var result]:
						doneSteps++;
						if (config is null || cfg == config)
						{
							steps[$"{container}/{step}"] = result;
						}
						break;
					case ["journey", _, _, _]:
						doneJourneys++;
						break;
				}
			}

			return new
			{
				doneJourneys,
				totalJourneys,
				doneSteps,
				totalSteps,
				steps,
			};
		}
	}
}
