using System.Buffers.Binary;
using System.Diagnostics;
using System.Xml.XPath;
using AwesomeAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp.PixelFormats;

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

	[TestCase(3, TestName = "Before Android added a colour-space word")]
	[TestCase(4, TestName = "With the colour-space word")]
	public void DecodeRawScreencapReadsThePixelsWhateverTheHeaderLength(int headerWords)
	{
		using var screen = AndroidPlatformConfig.DecodeRawScreencap(
			RawScreencap(headerWords, 1, [10, 20, 30, 255, 40, 50, 60, 255])
		);

		_ = screen.Width.Should().Be(2);
		_ = screen[0, 0].Should().Be(new Rgb24(10, 20, 30));
		_ = screen[1, 0].Should().Be(new Rgb24(40, 50, 60));
	}

	[Test]
	public void DecodeRawScreencapRefusesAFormatThatIsNotRgba() =>
		// 4 is PIXEL_FORMAT_RGB_565, two bytes a pixel, which read as RGBA would be garbage.
		_ = FluentActions
			.Invoking(() => AndroidPlatformConfig.DecodeRawScreencap(RawScreencap(4, 4, [0, 0, 0, 0])))
			.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*pixel format 4*");

	[Test]
	public void DecodeRawScreencapRefusesACaptureCutShort() =>
		_ = FluentActions
			.Invoking(() => AndroidPlatformConfig.DecodeRawScreencap(RawScreencap(4, 1, [10, 20, 30, 255, 40])))
			.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*not a 2x1 screen*");

	[Test]
	public void UptimeOfAProcessIsHowLongAgoItStarted()
	{
		using var started = Process.Start("sleep", "30");
		try
		{
			_ = AndroidPlatformConfig.UptimeOf(started.Id).Should().BeCloseTo(TimeSpan.Zero, TimeSpan.FromSeconds(10));
		}
		finally
		{
			started.Kill();
		}
	}

	[Test]
	public void UptimeOfAProcessThatIsGoneCannotBeTold()
	{
		using var started = Process.Start("true");
		started.WaitForExit();

		_ = AndroidPlatformConfig.UptimeOf(started.Id).Should().BeNull();
	}

	[Test]
	public void KillEndsAProcess()
	{
		using var started = Process.Start("sleep", "30");

		AndroidPlatformConfig.Kill(started.Id);

		_ = started.WaitForExit(TimeSpan.FromSeconds(5)).Should().BeTrue();
	}

	[Test]
	public void KillOfAProcessThatIsGoneDoesNothing()
	{
		using var started = Process.Start("true");
		started.WaitForExit();

		_ = FluentActions.Invoking(() => AndroidPlatformConfig.Kill(started.Id)).Should().NotThrow();
	}

	/// <summary>Builds what <c>screencap</c> writes for a 2x1 screen.</summary>
	/// <param name="headerWords">How many 32-bit words lead the pixels.</param>
	/// <param name="format">The pixel format the header declares.</param>
	/// <param name="pixels">The pixel bytes that follow.</param>
	/// <returns>The raw capture.</returns>
	private static byte[] RawScreencap(int headerWords, int format, byte[] pixels)
	{
		var raw = new byte[(headerWords * 4) + pixels.Length];
		BinaryPrimitives.WriteInt32LittleEndian(raw, 2);
		BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4), 1);
		BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(8), format);
		pixels.CopyTo(raw, headerWords * 4);
		return raw;
	}

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
