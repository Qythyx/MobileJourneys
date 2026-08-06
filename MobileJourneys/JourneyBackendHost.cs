using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace MobileJourneys;

/// <summary>
/// An HTTP host a fixture's app talks to, and the channel the harness drives that app through. One
/// host serves one fixture, each on its own port, so fixtures a suite runs concurrently never share
/// state.
/// <para>
/// A consumer subclasses this to answer its own app's routes. The control channel is
/// payload-agnostic — commands are queued and served as opaque JSON.
/// </para>
/// </summary>
/// <param name="configPath">
/// Path the app reads its start-up device state from, without a leading slash.
/// </param>
/// <param name="commandsPath">Path the app collects queued commands from, without a leading slash.</param>
public abstract class JourneyBackendHost(string configPath, string commandsPath) : IDisposable
{
	private readonly HttpListener _listener = new();
	private readonly CancellationTokenSource _stopping = new();
	private readonly ConcurrentQueue<string> _commands = new();
	private Task? _loop;

	/// <summary>The port this host listens on, chosen when <see cref="Start"/> claims it.</summary>
	public int Port { get; private set; }

	/// <summary>
	/// The URL the app should use as its backend. Loopback on purpose: an iOS simulator shares the
	/// host's loopback, and an Android emulator reaches it through <c>adb reverse</c> on the same
	/// port number.
	/// </summary>
	public string BaseUrl => $"http://127.0.0.1:{Port}";

	/// <summary>The device state the app adopts at start-up, serialized.</summary>
	public string ControlConfig { get; set; } = "{}";

	/// <summary>Cancelled when the host is disposed, for a route that deliberately never answers.</summary>
	protected CancellationToken Stopping => _stopping.Token;

	/// <summary>Queues an instruction for the app to collect on its next poll.</summary>
	/// <param name="commandJson">The serialized command.</param>
	public void QueueCommand(string commandJson) => _commands.Enqueue(commandJson);

	/// <summary>Discards commands the app never collected.</summary>
	public void ClearCommands() => _commands.Clear();

	/// <summary>Claims a port and begins answering requests on it.</summary>
	/// <param name="port">
	/// The port to bind, or 0 to take any free one. A fixed port is for a host something outside the
	/// suite has to find — a debugger launch configuration naming a URL once, say — where a port that
	/// moves every run is no use.
	/// </param>
	public void Start(int port = 0)
	{
		Port = port == 0 ? BindToFreePort() : Bind(port);
		_loop = Task.Run(AcceptLoopAsync);
	}

	private int Bind(int port)
	{
		_listener.Prefixes.Clear();
		_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		_listener.Start();
		return port;
	}

	/// <summary>
	/// Answers one request the control channel did not claim.
	/// </summary>
	/// <param name="context">The request to answer.</param>
	/// <param name="path">The request path, with the leading slash trimmed.</param>
	/// <returns>
	/// <c>true</c> when answered; <c>false</c> to send a 404. Answer by calling
	/// <see cref="WriteJsonAsync"/> or <see cref="Redirect"/>, each of which closes the response — write
	/// once, and do not touch it afterwards.
	/// </returns>
	protected abstract Task<bool> TryHandleAsync(HttpListenerContext context, string path);

	/// <summary>
	/// The body sent when a route throws, or when none matched. Overridden by a consumer whose app
	/// expects errors in a particular shape.
	/// </summary>
	/// <param name="message">What went wrong.</param>
	/// <returns>The response body, or <c>null</c> for none.</returns>
	protected virtual string? DescribeError(string message) => message;

	private int BindToFreePort()
	{
		while (true)
		{
			var candidate = FindFreePort();
			_listener.Prefixes.Clear();
			_listener.Prefixes.Add($"http://127.0.0.1:{candidate}/");
			try
			{
				_listener.Start();
				return candidate;
			}
			catch (HttpListenerException)
			{
				// Something claimed the port between probing it and binding it. Try another.
			}
		}
	}

	private static int FindFreePort()
	{
		var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

	private async Task AcceptLoopAsync()
	{
		while (!_stopping.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await _listener.GetContextAsync().ConfigureAwait(false);
			}
			catch (Exception) when (_stopping.IsCancellationRequested || !_listener.IsListening)
			{
				return;
			}

			// Each request on its own task: a held route blocks its handler for as long as the journey
			// keeps the screen up, and the app polls other routes meanwhile.
			_ = Task.Run(() => RespondAsync(context));
		}
	}

	private async Task RespondAsync(HttpListenerContext context)
	{
		try
		{
			var path = context.Request.Url!.AbsolutePath.TrimStart('/');
			if (await TryHandleControlAsync(context, path).ConfigureAwait(false))
			{
				return;
			}

			if (!await TryHandleAsync(context, path).ConfigureAwait(false))
			{
				await WriteJsonAsync(
						context.Response,
						HttpStatusCode.NotFound,
						DescribeError($"No route for '{path}'.")
					)
					.ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// The host is shutting down under a route that was deliberately never going to answer.
			context.Response.Abort();
		}
		catch (Exception ex)
		{
			await WriteJsonAsync(context.Response, HttpStatusCode.InternalServerError, DescribeError(ex.Message))
				.ConfigureAwait(false);
		}
	}

	private async Task<bool> TryHandleControlAsync(HttpListenerContext context, string path)
	{
		if (path == configPath)
		{
			await WriteJsonAsync(context.Response, HttpStatusCode.OK, ControlConfig).ConfigureAwait(false);
			return true;
		}

		if (path != commandsPath)
		{
			return false;
		}

		var queued = new List<string>();
		while (_commands.TryDequeue(out var command))
		{
			queued.Add(command);
		}

		await WriteJsonAsync(context.Response, HttpStatusCode.OK, $"[{string.Join(',', queued)}]")
			.ConfigureAwait(false);
		return true;
	}

	/// <summary>Sends a JSON body, or just a status when there is none.</summary>
	/// <param name="response">The response to write to.</param>
	/// <param name="status">The HTTP status to send.</param>
	/// <param name="json">The body, already serialized, or <c>null</c> for no body.</param>
	protected static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode status, string? json)
	{
		response.StatusCode = (int)status;
		if (json is not null)
		{
			response.ContentType = "application/json";
			var bytes = Encoding.UTF8.GetBytes(json);
			response.ContentLength64 = bytes.Length;
			await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
		}
		response.Close();
	}

	/// <summary>Sends a redirect.</summary>
	/// <param name="response">The response to write to.</param>
	/// <param name="location">Where to send the caller.</param>
	protected static void Redirect(HttpListenerResponse response, string location)
	{
		response.StatusCode = (int)HttpStatusCode.Found;
		response.RedirectLocation = location;
		response.Close();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_stopping.Cancel();
		// Close only. It unbinds and ends the accept loop on its own, where a preceding Stop() would
		// unbind first and leave Close() re-binding the now-free port to tear it down again — which
		// throws whenever anything else has claimed it in between.
		_listener.Close();
		try
		{
			_ = _loop?.Wait(TimeSpan.FromSeconds(5));
		}
		catch (AggregateException)
		{
			// The accept loop ends by having its listener closed underneath it.
		}
		_stopping.Dispose();
		GC.SuppressFinalize(this);
	}
}
