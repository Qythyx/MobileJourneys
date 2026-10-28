namespace MobileJourneys.Framework;

internal sealed record TestCase(PlatformConfig Config, JourneyDefinition Journey)
{
	public string Uid { get; } = $"{Config}.{Journey.Name}";

	public string DisplayName { get; } = $"{Config.DeviceName} · {Journey.Name}";
}
