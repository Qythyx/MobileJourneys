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

	/// <summary>
	/// Most recent lines of a rerun's console output kept as evidence. The page's live display is
	/// driven by the events the child posts back, but a child that fails to build posts none — so
	/// its output is retained and handed over when the process exits non-zero.
	/// </summary>
	private const int MaxJobLines = 400;

	private static readonly JsonSerializerOptions RequestOptions = new() { PropertyNameCaseInsensitive = true };

	private static readonly JsonSerializerOptions ResponseOptions = new();

	/// <summary>Guards <see cref="current"/> and the single-rerun-at-a-time rule.</summary>
	private static readonly Lock RerunGate = new();

	/// <summary>
	/// The most recent rerun, running or finished: the one a page can still address, and the one the
	/// next rerun replaces.
	/// </summary>
	private static RerunJob? current;

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
			case ["api", "run-events"] when context.Request.HttpMethod == "POST":
				ReceiveRunEvent(context);
				return;
			case ["api", "events"]:
				StreamEvents(context);
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

	private static RerunJob? FindJob(HttpListenerContext context, string parameterName)
	{
		var id = context.Request.QueryString[parameterName];
		lock (RerunGate)
		{
			return current is { } job && job.Id == id ? job : null;
		}
	}

	/// <summary>
	/// Takes one event from a running rerun and records it against that job, from where every open
	/// page picks it up. The body is the child's own JSON and is relayed unread — this server routes
	/// events, and the page is what interprets them.
	/// </summary>
	/// <param name="context">The request to read the event from and answer.</param>
	private static void ReceiveRunEvent(HttpListenerContext context)
	{
		using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
		var body = reader.ReadToEnd().Trim();
		if (FindJob(context, "job") is not { } job || body.Length == 0)
		{
			TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("unknown job"));
			return;
		}

		// Record before answering: the child posts its events one at a time and waits for each
		// response, so recording first is what makes the order it sends them the order they arrive in.
		job.Publish(body);
		TryRespond(context, 200, "text/plain", Encoding.UTF8.GetBytes("ok"));
	}

	/// <summary>
	/// Streams a job's events to the page as Server-Sent Events, replaying from the beginning so a
	/// reconnecting browser rebuilds the whole picture rather than resuming mid-run. The stream ends
	/// when the child process does, and the page closes it on the terminal event.
	/// </summary>
	/// <param name="context">The request to stream the events over.</param>
	private static void StreamEvents(HttpListenerContext context)
	{
		if (FindJob(context, "id") is not { } job)
		{
			TryRespond(context, 404, "text/plain", Encoding.UTF8.GetBytes("unknown job"));
			return;
		}

		try
		{
			context.Response.StatusCode = 200;
			context.Response.ContentType = "text/event-stream";
			context.Response.Headers["Cache-Control"] = "no-store";
			context.Response.SendChunked = true;
			for (var sent = 0; ; )
			{
				var (events, more) = job.Since(sent);
				foreach (var payload in events)
				{
					context.Response.OutputStream.Write(Encoding.UTF8.GetBytes($"data: {payload}\n\n"));
				}

				context.Response.OutputStream.Flush();
				sent += events.Count;
				if (more is null)
				{
					break;
				}

				more.Wait();
			}
		}
		catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
		{
			// The page navigated away or was closed mid-stream.
		}

		TryClose(context);
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
				var platform = config.FindPlatform(request?.Config);
				if (request is null || platform is null || request.Journeys is not { Count: > 0 })
				{
					TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("bad request"));
					return;
				}

				// A step several journeys walk keeps a failure artifact per journey, and the page resolves
				// the ones that failed identically together — so this names every journey it settles.
				if (!request.Journeys.All(journey => expected.IsExpectedStep(request.Container, request.Step, journey)))
				{
					TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("unknown step"));
					return;
				}

				if (action == "accept")
				{
					// One baseline serves every journey through the step, so it is written once — from the
					// capture of the journey whose artifacts the page was showing.
					var shown = new TestStep(platform, request.Container, request.Step, request.Journeys[0]);
					var newBytes = storage.ReadNewScreenshot(shown);
					if (newBytes is null)
					{
						TryRespond(context, 409, "text/plain", Encoding.UTF8.GetBytes("no .new capture on disk"));
						return;
					}

					storage.WriteBaseline(shown, newBytes);
				}

				foreach (var journey in request.Journeys)
				{
					storage.DeleteFailureArtifactsForStep(
						new TestStep(platform, request.Container, request.Step, journey)
					);
				}

				TryRespond(context, 200, "text/plain", Encoding.UTF8.GetBytes("ok"));
				return;
			}
			case "delete-extraneous":
			{
				var request = JsonSerializer.Deserialize<FileRequest>(body, RequestOptions);
				var platform = config.FindPlatform(request?.Config);
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
				StartRerunAll(context, config, JsonSerializer.Deserialize<RerunAllRequest>(body, RequestOptions));
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
			_ => config.FindPlatform(request.Config) is { } single ? [single] : [],
		};

		if (platforms.Count == 0)
		{
			TryRespond(context, 400, "text/plain", Encoding.UTF8.GetBytes("no matching platforms"));
			return;
		}

		List<string> scopeArgs = request.Scope switch
		{
			"all" => [],
			"failed" => ["--rerun"],
			_ => ["--filter", platforms[0].DisplayName],
		};

		LaunchRerun(
			context,
			config,
			$"{journey.Name} on {string.Join(", ", platforms.Select(p => p.DisplayName))}",
			[],
			["--journey", journey.Name, .. scopeArgs]
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
	/// <param name="config">Framework configuration providing the platforms the run reports against.</param>
	/// <param name="request">Whether to limit to failing journeys and/or rebuild the app first.</param>
	private static void StartRerunAll(HttpListenerContext context, FrameworkConfig config, RerunAllRequest? request)
	{
		var failed = request?.Failed ?? false;
		var embed = request?.Embed ?? false;
		LaunchRerun(
			context,
			config,
			(failed ? "failed journeys" : "all journeys") + (embed ? " (rebuilding app)" : ""),
			embed ? ["-p:EmbedAssemblies=true"] : [],
			failed ? ["--rerun"] : []
		);
	}

	/// <summary>
	/// Starts a <c>dotnet run</c> rerun with the given extra arguments appended to the shared ones,
	/// tracking it as the single active job. Only one rerun runs at a time — concurrent runs would
	/// fight over the simulators — so this answers 409 when one is already going.
	/// </summary>
	/// <param name="context">The request to answer with the new job's id, or an error.</param>
	/// <param name="config">Framework configuration providing the platforms the run reports against.</param>
	/// <param name="description">Human-readable summary of what is being rerun, shown on the page.</param>
	/// <param name="buildArgs">MSBuild properties for <c>dotnet run</c> itself, before the <c>--</c>.</param>
	/// <param name="runnerArgs">Arguments for the runner, after the <c>--</c> — the journey selection, or <c>--rerun</c>.</param>
	private static void LaunchRerun(
		HttpListenerContext context,
		FrameworkConfig config,
		string description,
		IEnumerable<string> buildArgs,
		IEnumerable<string> runnerArgs
	)
	{
		var projectPath = Directory.GetFiles(TestAssembly.ProjectRootPath, "*.csproj").FirstOrDefault();
		if (projectPath is null)
		{
			TryRespond(context, 500, "text/plain", Encoding.UTF8.GetBytes("test project not found"));
			return;
		}

		var console = new RerunConsole(config);
		var job = new RerunJob(console);
		lock (RerunGate)
		{
			if (current is { Running: true })
			{
				TryRespond(context, 409, "text/plain", Encoding.UTF8.GetBytes("a rerun is already running"));
				return;
			}

			current = job;
		}

		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = TestAssembly.ProjectRootPath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
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
		startInfo.ArgumentList.Add("--report-to");
		// Report back to the address the page reached this server on, which the request carries.
		startInfo.ArgumentList.Add(
			new UriBuilder(context.Request.Url!) { Path = "/api/run-events", Query = $"job={job.Id}" }
				.Uri
				.AbsoluteUri
		);
		foreach (var arg in runnerArgs)
		{
			startInfo.ArgumentList.Add(arg);
		}

		RerunConsole.Announce(description);
		job.Append($"$ dotnet {string.Join(" ", startInfo.ArgumentList.Select(Quote))}");
		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
		process.OutputDataReceived += (_, e) => job.Append(e.Data);
		process.ErrorDataReceived += (_, e) => job.Append(e.Data);
		process.Exited += (_, _) =>
		{
			// Exited can fire while the output handlers still have buffered lines to deliver, and those
			// lines are the whole evidence when the child died without reporting.
			process.WaitForExit();
			job.Complete(process.ExitCode);
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

		TryRespond(
			context,
			200,
			"application/json",
			Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = job.Id, description }, ResponseOptions))
		);
	}

	private static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;

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

	private static void TryClose(HttpListenerContext context)
	{
		try
		{
			context.Response.Close();
		}
		catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
		{
			// Already gone; closing it is all that was left to do anyway.
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

	private sealed record StepRequest(string Config, string Container, string Step, IReadOnlyList<string> Journeys);

	private sealed record FileRequest(string Config, string Container, string File);

	private sealed record RerunRequest(string Config, string Journey, string Scope);

	private sealed record RerunAllRequest(bool Failed, bool Embed);

	/// <summary>
	/// A running or finished <c>dotnet run</c> rerun: the events it has reported so far, and the
	/// console output collected from it.
	/// </summary>
	/// <param name="console">Shows the same run on the server's own console as it goes.</param>
	private sealed class RerunJob(RerunConsole console)
	{
		private readonly List<string> lines = [];
		private readonly List<string> events = [];
		private readonly Lock gate = new();

		/// <summary>Completed and replaced whenever an event arrives, so listeners can wait on the next one.</summary>
		private TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>Identifier the page subscribes to the event stream with.</summary>
		internal string Id { get; } = Guid.NewGuid().ToString("N");

		/// <summary><c>true</c> until the process exits.</summary>
		internal bool Running { get; private set; } = true;

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

			console.Line(line.TrimEnd());
		}

		/// <summary>Records an event the run reported, and wakes everyone waiting for one.</summary>
		/// <param name="json">The event as the run serialized it.</param>
		internal void Publish(string json)
		{
			lock (gate)
			{
				events.Add(json);
				Wake();
			}

			console.Consume(json);
		}

		/// <summary>
		/// Closes the job with a final event of the server's own: the process is gone, and this is the
		/// exit code it left. A run that reported nothing — one that failed to build, say — is only
		/// explicable from its console output, so a failing exit carries that too.
		/// </summary>
		/// <param name="exitCode">The exit code <c>dotnet run</c> returned.</param>
		internal void Complete(int exitCode)
		{
			lock (gate)
			{
				Running = false;
				events.Add(
					JsonSerializer.Serialize(
						new
						{
							type = "process-exited",
							exitCode,
							output = exitCode == 0 ? null : lines,
						},
						ResponseOptions
					)
				);
				Wake();
			}

			console.Finish(exitCode);
		}

		/// <summary>
		/// The events recorded since a listener's position, and a task completing when the next one
		/// arrives — <c>null</c> once the process has exited and everything has been handed over.
		/// </summary>
		/// <param name="sent">How many events the listener already has.</param>
		/// <returns>The events it does not, and what to wait on for more.</returns>
		internal (IReadOnlyList<string> Events, Task? More) Since(int sent)
		{
			lock (gate)
			{
				return ([.. events.Skip(sent)], Running ? arrived.Task : null);
			}
		}

		private void Wake()
		{
			var waiting = arrived;
			arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_ = waiting.TrySetResult();
		}
	}
}
