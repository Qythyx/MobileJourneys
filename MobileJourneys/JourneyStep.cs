using MobileJourneys.Actions;

namespace MobileJourneys;

/// <summary>
/// A single step in a journey test. Executes one action, verifies expectations in order,
/// then captures a screenshot and compares it against a baseline.
/// </summary>
/// <param name="Action">The single action to execute.</param>
/// <param name="Expect">Expectations to verify after the action, processed in array order.</param>
/// <param name="MaskElements">AutomationIds of elements whose bounds should be excluded from the
/// screenshot diff (e.g., animated spinners that differ between captures).</param>
/// <param name="PrefetchMasks"><c>true</c> to query the mask element IDs before running the
/// test action. This is useful if the action will cause the elements to become unavailable
/// such as showing a alert above them.</param>
public sealed record JourneyStep(
	JourneyAction Action,
	Expectation[]? Expect = null,
	string[]? MaskElements = null,
	bool PrefetchMasks = false
)
{
	/// <summary>
	/// Step name used in the baseline filename. Derived from the action, or from the first
	/// expectation when the action is <see cref="None"/>.
	/// </summary>
	public string Name => Action is None && Expect is [var first, ..] ? first.Name : Action.Name;
}
