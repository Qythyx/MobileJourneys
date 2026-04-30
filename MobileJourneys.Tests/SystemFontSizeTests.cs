using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class SystemFontSizeTests
{
	[Test]
	public void EnumHasTwelveValuesSevenStandardPlusFiveAccessibility()
	{
		var values = Enum.GetValues<SystemFontSize>();
		_ = values.Should().HaveCount(12);
	}

	[Test]
	public void EnumStandardSizesAreOrderedFromExtraSmallToExtraExtraExtraLarge()
	{
		// Ordering matters because consumers may compare via cast-to-int.
		_ = ((int)SystemFontSize.ExtraSmall).Should().BeLessThan((int)SystemFontSize.Small);
		_ = ((int)SystemFontSize.Small).Should().BeLessThan((int)SystemFontSize.Medium);
		_ = ((int)SystemFontSize.Medium).Should().BeLessThan((int)SystemFontSize.Large);
		_ = ((int)SystemFontSize.Large).Should().BeLessThan((int)SystemFontSize.ExtraLarge);
		_ = ((int)SystemFontSize.ExtraLarge).Should().BeLessThan((int)SystemFontSize.ExtraExtraLarge);
		_ = ((int)SystemFontSize.ExtraExtraLarge).Should().BeLessThan((int)SystemFontSize.ExtraExtraExtraLarge);
	}

	[Test]
	public void EnumAccessibilitySizesComeAfterStandardAndAreOrdered()
	{
		_ = ((int)SystemFontSize.ExtraExtraExtraLarge).Should().BeLessThan((int)SystemFontSize.AccessibilityMedium);
		_ = ((int)SystemFontSize.AccessibilityMedium).Should().BeLessThan((int)SystemFontSize.AccessibilityLarge);
		_ = ((int)SystemFontSize.AccessibilityLarge).Should().BeLessThan((int)SystemFontSize.AccessibilityExtraLarge);
		_ = ((int)SystemFontSize.AccessibilityExtraLarge)
			.Should()
			.BeLessThan((int)SystemFontSize.AccessibilityExtraExtraLarge);
		_ = ((int)SystemFontSize.AccessibilityExtraExtraLarge)
			.Should()
			.BeLessThan((int)SystemFontSize.AccessibilityExtraExtraExtraLarge);
	}
}
