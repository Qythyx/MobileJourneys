using System.Text.Json;

namespace MobileJourneys.Viewer;

/// <summary>
/// Builds the screenshot viewer's data manifest: the expected node tree (containers and
/// their steps), each journey's path through it, and — per platform — the baselines
/// present on disk, parsed failure artifacts, and extraneous files. Emitted as a
/// JavaScript assignment so the viewer page can load it with a plain script tag from
/// both the review server and the static filesystem.
/// </summary>
internal static class ViewerManifest
{
	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	/// <summary>Builds the manifest as a <c>window.MANIFEST = …;</c> JavaScript statement.</summary>
	/// <param name="config">Framework configuration providing the journeys, platforms, and storage.</param>
	internal static string BuildJs(FrameworkConfig config)
	{
		var expected = new ExpectedScreenshots(config.Journeys);

		// Nodes in first-seen (tree DFS) order, materializing ancestors that contribute no steps
		// of their own so every node's parent chain is present.
		var nodeOrder = new List<string>();
		var nodeSteps = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

		void EnsureNode(string container)
		{
			if (nodeSteps.ContainsKey(container))
			{
				return;
			}

			var slash = container.LastIndexOf('/');
			if (slash > 0)
			{
				EnsureNode(container[..slash]);
			}

			nodeSteps[container] = new(StringComparer.Ordinal);
			nodeOrder.Add(container);
		}

		foreach (var journey in config.Journeys)
		{
			foreach (var (container, stepName) in journey.ExpectedStepLocations())
			{
				EnsureNode(container);
				_ = nodeSteps[container].Add(stepName);
			}
		}

		var nodes = nodeOrder
			.Select(path =>
			{
				var slash = path.LastIndexOf('/');
				return new
				{
					path,
					name = slash > 0 ? path[(slash + 1)..] : path,
					parent = slash > 0 ? path[..slash] : null,
					steps = nodeSteps[path].ToList(),
				};
			})
			.ToList();

		var journeys = config.Journeys.ToDictionary(j => j.Name, j => j.Containers.ToList());

		var files = new Dictionary<string, object>();
		// Pixel dimensions of a representative baseline per platform, so the page can size every
		// screenshot thumbnail to one uniform box (the tallest device's aspect) — keeping node heights,
		// and therefore the whole layout, identical across devices.
		var dims = new Dictionary<string, object>();
		foreach (var platform in config.PlatformConfigs)
		{
			var baselines = new List<string>();
			var failures = new List<object>();
			var extraneous = new List<string>();
			(string Container, string File)? firstBaseline = null;
			// Maps "container/file" → a token that changes only when that file changes, so the page
			// can build cache-busting image URLs: unchanged files keep their URL (served from the
			// browser cache with no request), a changed file gets a new URL and is refetched.
			var versions = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var (container, fileName) in config.Storage.ListStoredFiles(platform))
			{
				versions[$"{container}/{fileName}"] = config.Storage.FileVersion(platform, container, fileName);

				if (!expected.IsExpected(container, fileName))
				{
					extraneous.Add($"{container}/{fileName}");
					continue;
				}

				if (ArtifactNaming.IsBaseline(fileName))
				{
					baselines.Add($"{container}/{fileName}");
					firstBaseline ??= (container, fileName);
					continue;
				}

				if (ArtifactNaming.ParseFailureArtifact(fileName) is { } parsed)
				{
					failures.Add(
						new
						{
							container,
							file = fileName,
							step = parsed.StepName,
							journey = parsed.JourneyName,
							kind = parsed.Kind,
							percent = parsed.DiffPercent,
							details = parsed.Kind == "fail"
								? ReadFailureDetails(config.Storage, platform, container, fileName)
								: string.Empty,
						}
					);
				}
			}

			baselines.Sort(StringComparer.Ordinal);
			extraneous.Sort(StringComparer.Ordinal);
			files[platform.DisplayName] = new
			{
				baselines,
				failures,
				extraneous,
				versions,
			};

			if (
				firstBaseline is { } fb
				&& TryReadDimensions(config.Storage, platform, fb.Container, fb.File) is { } size
			)
			{
				dims[platform.DisplayName] = new { w = size.Width, h = size.Height };
			}
		}

		var manifest = new
		{
			title = config.DisplayName,
			configs = config.PlatformConfigs.Select(p => p.DisplayName).ToList(),
			nodes,
			journeys,
			files,
			dims,
		};
		return $"window.MANIFEST = {JsonSerializer.Serialize(manifest, SerializerOptions)};";
	}

	/// <summary>
	/// Reads the full failure text embedded in a FAIL screenshot's PNG metadata. Returns empty for
	/// artifacts written before the details were stored (the filename still carries a short reason)
	/// and for the zero-byte marker written on a size mismatch.
	/// </summary>
	/// <param name="storage">Storage to read the artifact from.</param>
	/// <param name="platform">Platform fixture the artifact belongs to.</param>
	/// <param name="container">Container path holding the artifact.</param>
	/// <param name="fileName">The FAIL screenshot's filename.</param>
	private static string ReadFailureDetails(
		ScreenshotStorage storage,
		PlatformConfig platform,
		string container,
		string fileName
	)
	{
		var bytes = storage.ReadFile(platform, container, fileName);
		if (bytes is null or { Length: 0 })
		{
			return string.Empty;
		}

		try
		{
			using var image = SixLabors.ImageSharp.Image.Load(bytes);
			return ImageHelpers.GetFailureDetails(image);
		}
		catch (SixLabors.ImageSharp.ImageFormatException)
		{
			return string.Empty;
		}
	}

	/// <summary>
	/// Reads a screenshot's pixel dimensions from its PNG header (no full decode). Returns
	/// <c>null</c> if the file is missing, empty, or not a readable image.
	/// </summary>
	/// <param name="storage">Storage to read the screenshot from.</param>
	/// <param name="platform">Platform fixture the screenshot belongs to.</param>
	/// <param name="container">Container path holding the screenshot.</param>
	/// <param name="fileName">The screenshot's filename.</param>
	private static (int Width, int Height)? TryReadDimensions(
		ScreenshotStorage storage,
		PlatformConfig platform,
		string container,
		string fileName
	)
	{
		var bytes = storage.ReadFile(platform, container, fileName);
		if (bytes is null or { Length: 0 })
		{
			return null;
		}

		try
		{
			var info = SixLabors.ImageSharp.Image.Identify(bytes);
			return (info.Width, info.Height);
		}
		catch (SixLabors.ImageSharp.ImageFormatException)
		{
			return null;
		}
	}
}
