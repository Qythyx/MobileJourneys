namespace MobileUITests.Actions;

/// <summary>
/// No-op action. Use when a step needs to wait for expectations (e.g., an overlay
/// disappearing) without performing any interaction.
/// </summary>
public sealed record None() : JourneyAction
{
	public override void Execute(TestDriver driver) { }
}
