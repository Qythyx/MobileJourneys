namespace MobileJourneys;

/// <summary>
/// System-wide font size category. Mirrors iOS's
/// <see href="https://developer.apple.com/documentation/uikit/uicontentsizecategory">
/// UIContentSizeCategory</see>; on Android each value maps to a numeric
/// <see href="https://developer.android.com/reference/android/provider/Settings.System#FONT_SCALE">
/// Settings.System.FONT_SCALE</see> via <see cref="SimulatorHelper.SetSystemFontSize"/>.
/// The specific Android float values (0.82, 0.88, 0.94, 1.0, 1.15, 1.30, …) are chosen
/// to approximate iOS's categories — Android itself doesn't define a fixed enum, just
/// a multiplier.
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
