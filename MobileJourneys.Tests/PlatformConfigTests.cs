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
		"/path/to/app.app"
	);

	private static readonly AndroidPlatformConfig AndroidDark = new(
		"15",
		"Small Phone",
		"Pixel_API35",
		IsLightTheme: false,
		"com.example.app",
		"/path/Signed.apk",
		null
	);

	[Test]
	public void IosPlatformConfigReportsIosPlatformAndXCUITest()
	{
		_ = IosLight.Platform.Should().Be(TestPlatform.iOS);
		_ = IosLight.AutomationName.Should().Be("XCUITest");
	}

	[Test]
	public void AndroidPlatformConfigReportsAndroidPlatformAndUiAutomator2()
	{
		_ = AndroidDark.Platform.Should().Be(TestPlatform.Android);
		_ = AndroidDark.AutomationName.Should().Be("UiAutomator2");
	}

	[Test]
	public void DisplayNameFormatsAllFiveDimensions() =>
		IosLight.DisplayName.Should().Be("iOS · 26.2 · iPhone 17 Pro · light");

	[Test]
	public void DisplayNameDarkThemeAppearsAsDark() =>
		AndroidDark.DisplayName.Should().Be("Android · 15 · Small Phone · dark");

	[Test]
	public void ToStringEqualsDisplayName() => IosLight.ToString().Should().Be(IosLight.DisplayName);

	[Test]
	public void ColorToleranceIsHigherForAndroidThanIos() =>
		// Higher Android tolerance compensates for emulator rendering differences.
		AndroidDark.ColorTolerance.Should().BeGreaterThan(IosLight.ColorTolerance);

	[Test]
	public void ResolvedMainActivityFallsBackToAppIdentifierDotMainActivity() =>
		_ = AndroidDark.ResolvedMainActivity.Should().Be("com.example.app.MainActivity");

	[Test]
	public void ResolvedMainActivityUsesExplicitOverrideWhenProvided()
	{
		var withCustomActivity = AndroidDark with { MainActivity = "com.example.app.SomeOtherActivity" };
		_ = withCustomActivity.ResolvedMainActivity.Should().Be("com.example.app.SomeOtherActivity");
	}

	[Test]
	public void GetAlertButtonLocatorNoQuotesUsesSingleQuotedLiteral()
	{
		var locator = AndroidDark.GetAlertButtonLocator("OK").ToString();
		_ = locator.Should().Contain("='ok'");
	}

	[Test]
	public void GetAlertButtonLocatorLabelWithApostropheUsesDoubleQuotedLiteral()
	{
		// "Don't" lowercases to "don't" — the apostrophe used to break the single-quoted XPath.
		var locator = AndroidDark.GetAlertButtonLocator("Don't").ToString();
		_ = locator.Should().Contain("=\"don't\"");
	}

	[Test]
	public void GetAlertButtonLocatorLabelWithBothQuotesUsesConcatExpression()
	{
		// Pathological label containing both ' and " forces concat() escaping.
		var locator = AndroidDark.GetAlertButtonLocator("a'b\"c").ToString();
		_ = locator.Should().Contain("concat(");
	}

	[Test]
	public void ResolveAppBinaryPathReturnsPathWhenBinaryExists()
	{
		// Use the test assembly itself as a stand-in "existing binary".
		var existing = typeof(PlatformConfigTests).Assembly.Location;
		var config = AndroidDark with { AppBinaryPath = existing };

		_ = config.ResolveAppBinaryPath().Should().Be(existing);
	}

	[Test]
	public void ResolveAppBinaryPathThrowsWithPathAndPlatformWhenAndroidBinaryMissing()
	{
		// Lock in the user-facing message — both the bad path and the platform name must
		// appear so consumers can diagnose typos / forgotten rebuilds without a debugger.
		var config = AndroidDark with
		{
			AppBinaryPath = "/does/not/exist/Signed.apk",
		};
		Action act = () => config.ResolveAppBinaryPath();

		var ex = act.Should().Throw<FileNotFoundException>().Which;
		_ = ex.Message.Should().Contain("/does/not/exist/Signed.apk");
		_ = ex.Message.Should().Contain("Android");
	}

	[Test]
	public void ResolveAppBinaryPathThrowsWithPathAndPlatformWhenIosBinaryMissing()
	{
		var config = IosLight with { AppBinaryPath = "/does/not/exist/MyApp.app" };
		Action act = () => config.ResolveAppBinaryPath();

		var ex = act.Should().Throw<FileNotFoundException>().Which;
		_ = ex.Message.Should().Contain("/does/not/exist/MyApp.app");
		_ = ex.Message.Should().Contain("iOS");
	}
}
