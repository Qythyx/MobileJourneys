using System.Drawing;
using OpenQA.Selenium.Appium;

namespace MobileJourneys;

/// <summary>
/// Dictionary / Appium-script utility extensions used to read structured results from
/// Appium <c>mobile:</c> commands.
/// </summary>
public static class SimulatorHelper
{
	/// <summary>Gets a nested dictionary value by key. Throws if the value isn't a dictionary.</summary>
	/// <param name="parent">The parent dictionary.</param>
	/// <param name="key">Key to look up.</param>
	public static Dictionary<string, object> GetDict(this Dictionary<string, object> parent, string key) =>
		(Dictionary<string, object>)parent[key];

	/// <summary>Gets a value by key and converts it to <see cref="int"/>.</summary>
	/// <param name="parent">The parent dictionary.</param>
	/// <param name="key">Key to look up.</param>
	public static int GetInt(this Dictionary<string, object> parent, string key) => GetInt(parent[key]);

	/// <summary>Reads <c>left</c>/<c>top</c>/<c>width</c>/<c>height</c> int keys into a <see cref="Rectangle"/>.</summary>
	/// <param name="parent">Dictionary with the four bounds keys.</param>
	public static Rectangle GetRectangle(this Dictionary<string, object> parent) =>
		new(parent.GetInt("left"), parent.GetInt("top"), parent.GetInt("width"), parent.GetInt("height"));

	/// <summary>Runs an Appium script that returns a dictionary, casting the result.</summary>
	/// <param name="app">Appium driver.</param>
	/// <param name="script">Script name (e.g., <c>"mobile: viewportRect"</c>).</param>
	public static Dictionary<string, object> GetDict(this AppiumDriver app, string script) =>
		(Dictionary<string, object>)(
			app.ExecuteScript(script) ?? throw new InvalidOperationException("Script did not return expected data")
		);

	private static int GetInt(object obj) => Convert.ToInt32(obj, System.Globalization.CultureInfo.InvariantCulture);
}
