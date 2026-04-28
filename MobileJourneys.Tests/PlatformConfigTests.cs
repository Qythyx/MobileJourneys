using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class PlatformConfigTests
{
	private static readonly IosPlatformConfig IosLight = new(
		"26.2",
		"iPhone 17 Pro",
		IsLightTheme: true,
		"com.example.app",
		"/path/to/app.app",
		MaxScreenshotHeight: 2000
	);

	private static readonly AndroidPlatformConfig AndroidDark = new(
		"15",
		"Small Phone",
		"Pixel_API35",
		IsLightTheme: false,
		"com.example.app",
		"/path/Signed.apk",
		null,
		MaxScreenshotHeight: 2000
	);

	[Test]
	public void IosPlatformConfig_Reports_IosPlatformAndXCUITest()
	{
		IosLight.Platform.Should().Be(TestPlatform.iOS);
		IosLight.AutomationName.Should().Be("XCUITest");
	}

	[Test]
	public void AndroidPlatformConfig_Reports_AndroidPlatformAndUiAutomator2()
	{
		AndroidDark.Platform.Should().Be(TestPlatform.Android);
		AndroidDark.AutomationName.Should().Be("UiAutomator2");
	}

	[Test]
	public void DisplayName_FormatsAllFiveDimensions() =>
		IosLight.DisplayName.Should().Be("iOS · 26.2 · iPhone 17 Pro · light");

	[Test]
	public void DisplayName_DarkThemeAppearsAsDark() =>
		AndroidDark.DisplayName.Should().Be("Android · 15 · Small Phone · dark");

	[Test]
	public void ToString_EqualsDisplayName() => IosLight.ToString().Should().Be(IosLight.DisplayName);

	[Test]
	public void ColorTolerance_IsHigherForAndroidThanIos() =>
		// Higher Android tolerance compensates for emulator rendering differences.
		AndroidDark.ColorTolerance.Should().BeGreaterThan(IosLight.ColorTolerance);

	[Test]
	public void MaxScreenshotHeight_IsAvailableOnBothPlatforms()
	{
		IosLight.MaxScreenshotHeight.Should().Be(2000);
		AndroidDark.MaxScreenshotHeight.Should().Be(2000);
	}

	[Test]
	public void ResolvedMainActivity_FallsBackToAppIdentifierDotMainActivity()
	{
		AndroidDark.ResolvedMainActivity.Should().Be("com.example.app.MainActivity");
	}

	[Test]
	public void ResolvedMainActivity_UsesExplicitOverrideWhenProvided()
	{
		var withCustomActivity = AndroidDark with { MainActivity = "com.example.app.SomeOtherActivity" };
		withCustomActivity.ResolvedMainActivity.Should().Be("com.example.app.SomeOtherActivity");
	}
}
