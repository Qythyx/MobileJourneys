namespace MobileJourneys;

/// <summary>
/// System-wide font size category. Mirrors iOS's
/// <see href="https://developer.apple.com/documentation/uikit/uicontentsizecategory">
/// UIContentSizeCategory</see>, which is the source-of-truth for both names and ordering.
/// </summary>
/// <remarks>
/// Android has no equivalent enum: <c>Settings.System.FONT_SCALE</c> is a free-form float, and
/// its Settings app offers seven presets — 0.85, 1.0, 1.15, 1.30, 1.50, 1.80 and 2.0 from
/// Android 14 on. Each category maps to the nearest of them, so several categories share a
/// scale, and the largest ones all reach Android's largest. See the platform-specific
/// <c>SetSystemFontSize</c> overrides on <see cref="PlatformConfig"/> for the exact mapping.
/// </remarks>
public enum SystemFontSize
{
	/// <summary>iOS: extra-small. Android: 0.85.</summary>
	ExtraSmall,

	/// <summary>iOS: small. Android: 0.85.</summary>
	Small,

	/// <summary>iOS: medium. Android: 1.0.</summary>
	Medium,

	/// <summary>iOS: large (default). Android: 1.0 (default).</summary>
	Large,

	/// <summary>iOS: extra-large. Android: 1.15.</summary>
	ExtraLarge,

	/// <summary>iOS: extra-extra-large. Android: 1.30.</summary>
	ExtraExtraLarge,

	/// <summary>iOS: extra-extra-extra-large. Android: 1.50.</summary>
	ExtraExtraExtraLarge,

	/// <summary>iOS: accessibility-medium. Android: 1.50.</summary>
	AccessibilityMedium,

	/// <summary>iOS: accessibility-large. Android: 1.8.</summary>
	AccessibilityLarge,

	/// <summary>iOS: accessibility-extra-large. Android: 2.0.</summary>
	AccessibilityExtraLarge,

	/// <summary>iOS: accessibility-extra-extra-large. Android: 2.0.</summary>
	AccessibilityExtraExtraLarge,

	/// <summary>iOS: accessibility-extra-extra-extra-large. Android: 2.0.</summary>
	AccessibilityExtraExtraExtraLarge,
}
