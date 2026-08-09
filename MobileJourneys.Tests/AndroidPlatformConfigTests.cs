using System.Xml.XPath;
using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class AndroidPlatformConfigTests
{
	private static readonly AndroidPlatformConfig Config = new(
		"15",
		"Pixel",
		"avd",
		IsLightTheme: true,
		"com.example.app",
		"/path/app.apk",
		null,
		200,
		550,
		3 * 10,
		0.005
	);

	[TestCase(SystemFontSize.ExtraSmall, 0.82)]
	[TestCase(SystemFontSize.Small, 0.88)]
	[TestCase(SystemFontSize.Medium, 0.94)]
	[TestCase(SystemFontSize.Large, 1.0)]
	[TestCase(SystemFontSize.ExtraLarge, 1.15)]
	[TestCase(SystemFontSize.ExtraExtraLarge, 1.30)]
	[TestCase(SystemFontSize.ExtraExtraExtraLarge, 1.45)]
	[TestCase(SystemFontSize.AccessibilityMedium, 1.6)]
	[TestCase(SystemFontSize.AccessibilityLarge, 1.8)]
	[TestCase(SystemFontSize.AccessibilityExtraLarge, 2.0)]
	[TestCase(SystemFontSize.AccessibilityExtraExtraLarge, 2.25)]
	[TestCase(SystemFontSize.AccessibilityExtraExtraExtraLarge, 2.5)]
	public void ToAndroidFontScaleMapsEveryEnumValue(SystemFontSize size, double expected) =>
		AndroidPlatformConfig.ToAndroidFontScale(size).Should().Be(expected);

	[Test]
	public void ToAndroidFontScaleThrowsForUndefinedEnumValue() =>
		_ = FluentActions
			.Invoking(() => AndroidPlatformConfig.ToAndroidFontScale((SystemFontSize)999))
			.Should()
			.Throw<ArgumentOutOfRangeException>();

	[Test]
	public void VerifyDependenciesFailsWithHelpfulMessageWhenAdbMissingFromAndroidHome()
	{
		// Point ANDROID_HOME at a temp dir without platform-tools/adb so the missing-binary
		// branch is exercised regardless of whether the host actually has the SDK installed.
		var saved = Environment.GetEnvironmentVariable("ANDROID_HOME");
		var tempHome = Directory.CreateTempSubdirectory("android_home_").FullName;
		Environment.SetEnvironmentVariable("ANDROID_HOME", tempHome);
		try
		{
			Action act = Config.VerifyDependencies;

			var ex = act.Should().Throw<InvalidOperationException>().Which;
			_ = ex.Message.Should().Contain("adb not found at");
			_ = ex.Message.Should().Contain(tempHome);
		}
		finally
		{
			Environment.SetEnvironmentVariable("ANDROID_HOME", saved);
			Directory.Delete(tempHome, recursive: true);
		}
	}

	[Test]
	public void ParseAttachedDevicesReadsSerialsAndSkipsTheHeader() =>
		_ = AndroidPlatformConfig
			.ParseAttachedDevices("List of devices attached\nemulator-5554\tdevice\nemulator-5556\tdevice\n")
			.Should()
			.Equal("emulator-5554", "emulator-5556");

	[Test]
	public void ParseAttachedDevicesIncludesAnOfflineDeviceBecauseItMayStillBeBooting() =>
		_ = AndroidPlatformConfig
			.ParseAttachedDevices("List of devices attached\nemulator-5554\toffline\n")
			.Should()
			.Equal("emulator-5554");

	[TestCase("List of devices attached\n")]
	[TestCase("List of devices attached\n0123456789ABCDEF\tunauthorized\n")]
	[TestCase("List of devices attached\n0123456789ABCDEF\tno permissions; see [https://developer.android.com]\n")]
	public void ParseAttachedDevicesIgnoresDevicesThatWillNotBecomeUsable(string output) =>
		_ = AndroidPlatformConfig.ParseAttachedDevices(output).Should().BeEmpty();

	[Test]
	public void GetAlertButtonLocatorSimpleLabelProducesValidXPath() => _ = Compile(Config.GetAlertButtonLocator("OK"));

	[Test]
	public void GetAlertButtonLocatorApostropheLabelProducesValidXPath() =>
		// "Don't" hits the double-quoted-literal branch.
		_ = Compile(Config.GetAlertButtonLocator("Don't"));

	[Test]
	public void GetAlertButtonLocatorBothQuoteLabelProducesValidXPath() =>
		// Pathological label with both ' and " forces the concat() branch.
		_ = Compile(Config.GetAlertButtonLocator("a'b\"c"));

	private static XPathExpression Compile(OpenQA.Selenium.By by)
	{
		// By.XPath(...).ToString() returns "By.XPath: <expr>". Strip the prefix so the
		// raw expression can be validated by the XPath parser.
		const string Prefix = "By.XPath: ";
		var raw = by.ToString() ?? string.Empty;
		var expr = raw.StartsWith(Prefix, StringComparison.Ordinal) ? raw[Prefix.Length..] : raw;
		return XPathExpression.Compile(expr);
	}
}
