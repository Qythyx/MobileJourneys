using System.Text;

namespace MobileJourneys.Framework;

/// <summary>
/// Appends one line per completed step (and per completed journey) to a file the review server hands
/// the run via the <c>MJ_PROGRESS_FILE</c> environment variable, so the viewer page can show
/// per-screenshot progress in real time while a rerun is in flight. Never constructed (a no-op) when
/// the variable is unset, so ordinary <c>dotnet test</c> runs are unaffected. Thread-safe: the
/// platform lanes run concurrently and append from different threads.
/// </summary>
internal sealed class ProgressLog
{
	private readonly string path;
	private readonly object gate = new();

	private ProgressLog(string path) => this.path = path;

	/// <summary>Returns a log writing to <c>MJ_PROGRESS_FILE</c>, or <c>null</c> when it is unset.</summary>
	public static ProgressLog? FromEnvironment()
	{
		var path = Environment.GetEnvironmentVariable("MJ_PROGRESS_FILE");
		return string.IsNullOrEmpty(path) ? null : new ProgressLog(path);
	}

	/// <summary>
	/// Records that a step finished, as a tab-separated line: <c>step\tconfig\tcontainer\tstep\tpass|fail</c>.
	/// The container/step identify the same screenshot the viewer renders, so the page can recolor it.
	/// </summary>
	/// <param name="step">The step's screenshot identity (platform, container, numbered step name).</param>
	/// <param name="passed"><c>true</c> if the step's screenshot matched its baseline.</param>
	public void StepCompleted(TestStep step, bool passed) =>
		Append($"step\t{step.Config.DisplayName}\t{step.Container}\t{step.StepName}\t{(passed ? "pass" : "fail")}");

	/// <summary>
	/// Records that a whole journey finished, as a tab-separated line: <c>journey\tconfig\tjourney\tpass|fail</c>.
	/// Used only for the "N/M journeys" counter; per-screenshot coloring comes from the step lines.
	/// </summary>
	/// <param name="testCase">The journey and platform that finished.</param>
	/// <param name="passed"><c>true</c> if every step in the journey passed.</param>
	public void JourneyCompleted(TestCase testCase, bool passed) =>
		Append($"journey\t{testCase.Config.DisplayName}\t{testCase.Journey.Name}\t{(passed ? "pass" : "fail")}");

	private void Append(string line)
	{
		lock (gate)
		{
			File.AppendAllText(path, line + "\n", Encoding.UTF8);
		}
	}
}
