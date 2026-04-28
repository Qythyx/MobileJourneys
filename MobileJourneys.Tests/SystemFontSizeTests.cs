using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class SystemFontSizeTests
{
	[Test]
	public void Enum_HasTwelveValues_SevenStandardPlusFiveAccessibility()
	{
		var values = Enum.GetValues<SystemFontSize>();
		values.Should().HaveCount(12);
	}

	[Test]
	public void Enum_StandardSizesAreOrdered_FromExtraSmallToExtraExtraExtraLarge()
	{
		// Ordering matters because consumers may compare via cast-to-int.
		((int)SystemFontSize.ExtraSmall)
			.Should()
			.BeLessThan((int)SystemFontSize.Small);
		((int)SystemFontSize.Small).Should().BeLessThan((int)SystemFontSize.Medium);
		((int)SystemFontSize.Medium).Should().BeLessThan((int)SystemFontSize.Large);
		((int)SystemFontSize.Large).Should().BeLessThan((int)SystemFontSize.ExtraLarge);
		((int)SystemFontSize.ExtraLarge).Should().BeLessThan((int)SystemFontSize.ExtraExtraLarge);
		((int)SystemFontSize.ExtraExtraLarge).Should().BeLessThan((int)SystemFontSize.ExtraExtraExtraLarge);
	}

	[Test]
	public void Enum_AccessibilitySizesComeAfterStandard_AndAreOrdered()
	{
		((int)SystemFontSize.ExtraExtraExtraLarge).Should().BeLessThan((int)SystemFontSize.AccessibilityMedium);
		((int)SystemFontSize.AccessibilityMedium).Should().BeLessThan((int)SystemFontSize.AccessibilityLarge);
		((int)SystemFontSize.AccessibilityLarge).Should().BeLessThan((int)SystemFontSize.AccessibilityExtraLarge);
		((int)SystemFontSize.AccessibilityExtraLarge)
			.Should()
			.BeLessThan((int)SystemFontSize.AccessibilityExtraExtraLarge);
		((int)SystemFontSize.AccessibilityExtraExtraLarge)
			.Should()
			.BeLessThan((int)SystemFontSize.AccessibilityExtraExtraExtraLarge);
	}
}
