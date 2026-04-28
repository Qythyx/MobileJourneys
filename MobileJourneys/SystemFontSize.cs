namespace MobileJourneys;

/// <summary>
/// System-wide font size category. Mirrors iOS's UIContentSizeCategory; on Android
/// each value maps to a numeric <c>font_scale</c> setting via
/// <see cref="SimulatorHelper.SetSystemFontSize"/>.
/// </summary>
public enum SystemFontSize
{
	/// <summary>iOS: extra-small. Android: 0.82.</summary>
	ExtraSmall,

	/// <summary>iOS: small. Android: 0.88.</summary>
	Small,

	/// <summary>iOS: medium. Android: 0.94.</summary>
	Medium,

	/// <summary>iOS: large (default). Android: 1.0 (default).</summary>
	Large,

	/// <summary>iOS: extra-large. Android: 1.15.</summary>
	ExtraLarge,

	/// <summary>iOS: extra-extra-large. Android: 1.30.</summary>
	ExtraExtraLarge,

	/// <summary>iOS: extra-extra-extra-large. Android: 1.45.</summary>
	ExtraExtraExtraLarge,

	/// <summary>iOS: accessibility-medium. Android: 1.6.</summary>
	AccessibilityMedium,

	/// <summary>iOS: accessibility-large. Android: 1.8.</summary>
	AccessibilityLarge,

	/// <summary>iOS: accessibility-extra-large. Android: 2.0.</summary>
	AccessibilityExtraLarge,

	/// <summary>iOS: accessibility-extra-extra-large. Android: 2.25.</summary>
	AccessibilityExtraExtraLarge,

	/// <summary>iOS: accessibility-extra-extra-extra-large. Android: 2.5.</summary>
	AccessibilityExtraExtraExtraLarge,
}
