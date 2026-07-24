using System.Runtime.CompilerServices;
using MobileJourneys.Actions;
using MobileJourneys.Expectations;

namespace MobileJourneys;

/// <summary>
/// Factory helpers for the journey DSL, imported with <c>using static MobileJourneys.Dsl;</c> so a
/// journey forest reads as bare calls — <c>Branch("Menu", Step(Tap(id)))</c> — instead of a wall of
/// <c>new</c>. Each helper is a thin wrapper over the matching constructor. Because every helper
/// returns the concrete type, arguments are never target-typed <c>new()</c> expressions, so nested
/// calls compose without the bracketing the raw constructors would need to disambiguate them.
/// </summary>
public static class Dsl
{
	#region Tree assembly

	/// <summary>Creates a terminal (leaf) <see cref="MobileJourneys.Branch"/> — a journey named after
	/// the node, with no children.</summary>
	/// <param name="name">Folder name for this node's screenshots, and the journey's name.</param>
	/// <param name="steps">The steps unique to this journey.</param>
	public static Branch Branch(string name, params JourneyStep[] steps) => new(name, steps, []);

	/// <summary>Creates an interior <see cref="MobileJourneys.Branch"/> — a prefix of shared steps
	/// followed by the child nodes that diverge after them.</summary>
	/// <param name="name">Folder name for this node's screenshots.</param>
	/// <param name="steps">The steps shared by every journey beneath this node.</param>
	/// <param name="children">The nodes that diverge after this node's steps.</param>
	public static Branch Branch(string name, JourneyStep[] steps, Branch[] children) => new(name, steps, children);

	/// <summary>Creates a <see cref="JourneyStep"/> that runs an action with no expectations.</summary>
	/// <param name="action">The action to execute.</param>
	public static JourneyStep Step(JourneyAction action) => new(action);

	/// <summary>Creates a <see cref="JourneyStep"/> that runs an action then verifies expectations.</summary>
	/// <param name="action">The action to execute.</param>
	/// <param name="expect">Expectations to verify after the action, in order.</param>
	public static JourneyStep Step(JourneyAction action, params Expectation[] expect) => new(action, expect);

	/// <summary>Creates a <see cref="JourneyStep"/> whose screenshot excludes the given mask elements.</summary>
	/// <param name="action">The action to execute.</param>
	/// <param name="expect">Expectations to verify after the action, in order.</param>
	/// <param name="maskElements">AutomationIds of elements excluded from the screenshot diff.</param>
	public static JourneyStep Step(JourneyAction action, Expectation[] expect, string[] maskElements) =>
		new(action, expect, maskElements);

	/// <summary>Creates a <see cref="JourneyStep"/> that queries its mask elements before the action
	/// runs, for actions that make those elements unavailable (e.g. an alert covering them).</summary>
	/// <param name="action">The action to execute.</param>
	/// <param name="expect">Expectations to verify after the action, in order.</param>
	/// <param name="maskElements">AutomationIds of elements excluded from the screenshot diff.</param>
	/// <param name="prefetchMasks"><c>true</c> to query the mask elements before running the action.</param>
	public static JourneyStep Step(
		JourneyAction action,
		Expectation[] expect,
		string[] maskElements,
		bool prefetchMasks
	) => new(action, expect, maskElements, prefetchMasks);

	/// <summary>Creates a <see cref="JourneyTree"/> root sharing one scenario and start state.</summary>
	/// <param name="scenario">The mock backend configuration for every journey in the tree.</param>
	/// <param name="initialExpect">Expectations for the initial screen before any steps execute.</param>
	/// <param name="steps">Steps common to every journey in the tree.</param>
	/// <param name="children">The nodes that diverge after the root's steps; empty for a single-journey tree.</param>
	/// <param name="name">Auto-populated from the field name via CallerMemberName.</param>
	public static JourneyTree Tree(
		IJourneyEnvironment scenario,
		Expectation[] initialExpect,
		JourneyStep[] steps,
		Branch[] children,
		[CallerMemberName] string name = ""
	) => new(scenario, initialExpect, steps, children, null, name);

	/// <summary>Creates a <see cref="JourneyTree"/> root whose initial screenshot excludes the given
	/// mask elements.</summary>
	/// <param name="scenario">The mock backend configuration for every journey in the tree.</param>
	/// <param name="initialExpect">Expectations for the initial screen before any steps execute.</param>
	/// <param name="steps">Steps common to every journey in the tree.</param>
	/// <param name="children">The nodes that diverge after the root's steps; empty for a single-journey tree.</param>
	/// <param name="initialMaskElements">AutomationIds excluded from the initial screenshot's diff.</param>
	/// <param name="name">Auto-populated from the field name via CallerMemberName.</param>
	public static JourneyTree Tree(
		IJourneyEnvironment scenario,
		Expectation[] initialExpect,
		JourneyStep[] steps,
		Branch[] children,
		string[] initialMaskElements,
		[CallerMemberName] string name = ""
	) => new(scenario, initialExpect, steps, children, initialMaskElements, name);

	#endregion Tree assembly

	#region Actions

	/// <summary>Creates a <see cref="Actions.Tap"/> action.</summary>
	/// <param name="automationId">AutomationId of the element to tap.</param>
	public static Tap Tap(string automationId) => new(automationId);

	/// <summary>Creates a <see cref="Actions.TypeText"/> action.</summary>
	/// <param name="automationId">AutomationId of the element to type into.</param>
	/// <param name="text">The text to type.</param>
	public static TypeText TypeText(string automationId, string text) => new(automationId, text);

	/// <summary>Creates a <see cref="Actions.None"/> action, which does nothing but lets a step assert
	/// expectations or capture a screenshot.</summary>
	public static None None() => new();

	/// <summary>Creates a <see cref="Actions.DismissAlert"/> action.</summary>
	public static DismissAlert DismissAlert() => new();

	/// <summary>Creates a <see cref="Actions.DismissKeyboard"/> action.</summary>
	public static DismissKeyboard DismissKeyboard() => new();

	/// <summary>Creates a <see cref="Actions.ScrollToElement"/> action.</summary>
	/// <param name="automationId">AutomationId of the element to scroll into view.</param>
	public static ScrollToElement ScrollToElement(string automationId) => new(automationId);

	/// <summary>Creates a <see cref="Actions.SwipeLeft"/> action.</summary>
	/// <param name="automationId">AutomationId of the element to swipe left on.</param>
	public static SwipeLeft SwipeLeft(string automationId) => new(automationId);

	/// <summary>Creates a <see cref="Actions.SwipeRight"/> action.</summary>
	/// <param name="automationId">AutomationId of the element to swipe right on.</param>
	public static SwipeRight SwipeRight(string automationId) => new(automationId);

	/// <summary>Creates a <see cref="Actions.SetSystemFontSize"/> action.</summary>
	/// <param name="size">The system font size to apply.</param>
	public static SetSystemFontSize SetSystemFontSize(SystemFontSize size) => new(size);

	/// <summary>Creates an <see cref="Actions.InvertSystemTheme"/> action.</summary>
	public static InvertSystemTheme InvertSystemTheme() => new();

	/// <summary>Creates a <see cref="Actions.TapNotification"/> action.</summary>
	public static TapNotification TapNotification() => new();

	/// <summary>Creates a <see cref="Actions.TapAlertButton"/> action.</summary>
	/// <param name="buttonLabel">The label of the alert button to tap.</param>
	public static TapAlertButton TapAlertButton(string buttonLabel) => new(buttonLabel);

	#endregion Actions

	#region Expectations

	/// <summary>Creates a <see cref="Expectations.Found"/> expectation with the default timeout.</summary>
	/// <param name="automationId">AutomationId of the element that must be found.</param>
	public static Found Found(string automationId) => new(automationId);

	/// <summary>Creates a <see cref="Expectations.Found"/> expectation with an explicit timeout.</summary>
	/// <param name="automationId">AutomationId of the element that must be found.</param>
	/// <param name="timeoutSeconds">Maximum seconds to wait before failing.</param>
	public static Found Found(string automationId, int timeoutSeconds) => new(automationId, timeoutSeconds);

	/// <summary>Creates a <see cref="Expectations.NotFound"/> expectation.</summary>
	/// <param name="automationId">AutomationId of the element that must be absent.</param>
	public static NotFound NotFound(string automationId) => new(automationId);

	/// <summary>Creates a <see cref="Expectations.FoundWithText"/> expectation.</summary>
	/// <param name="automationId">AutomationId of the element that must be found.</param>
	/// <param name="expectedTexts">One or more text values the element may contain (any match succeeds).</param>
	public static FoundWithText FoundWithText(string automationId, params string[] expectedTexts) =>
		new(automationId, expectedTexts);

	/// <summary>Creates an <see cref="Expectations.AlertFound"/> expectation.</summary>
	public static AlertFound AlertFound() => new();

	/// <summary>Creates a <see cref="Expectations.WaitForNotification"/> expectation.</summary>
	public static WaitForNotification WaitForNotification() => new();

	#endregion Expectations
}
