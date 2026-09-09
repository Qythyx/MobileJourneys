using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class IosPlatformConfigTests
{
	[TestCase(SystemFontSize.ExtraSmall, "extra-small")]
	[TestCase(SystemFontSize.Small, "small")]
	[TestCase(SystemFontSize.Medium, "medium")]
	[TestCase(SystemFontSize.Large, "large")]
	[TestCase(SystemFontSize.ExtraLarge, "extra-large")]
	[TestCase(SystemFontSize.ExtraExtraLarge, "extra-extra-large")]
	[TestCase(SystemFontSize.ExtraExtraExtraLarge, "extra-extra-extra-large")]
	[TestCase(SystemFontSize.AccessibilityMedium, "accessibility-medium")]
	[TestCase(SystemFontSize.AccessibilityLarge, "accessibility-large")]
	[TestCase(SystemFontSize.AccessibilityExtraLarge, "accessibility-extra-large")]
	[TestCase(SystemFontSize.AccessibilityExtraExtraLarge, "accessibility-extra-extra-large")]
	[TestCase(SystemFontSize.AccessibilityExtraExtraExtraLarge, "accessibility-extra-extra-extra-large")]
	public void ToIosContentSizeMapsEveryEnumValue(SystemFontSize size, string expected) =>
		IosPlatformConfig.ToIosContentSize(size).Should().Be(expected);

	[Test]
	public void ToIosContentSizeThrowsForUndefinedEnumValue() =>
		_ = FluentActions
			.Invoking(() => IosPlatformConfig.ToIosContentSize((SystemFontSize)999))
			.Should()
			.Throw<ArgumentOutOfRangeException>();

	[Test]
	public void ReadCrashLogAtReturnsNullWhenLogFileMissing()
	{
		using var temp = new TempContainer();

		_ = IosPlatformConfig.ReadCrashLogAt(temp.Path).Should().BeNull();
	}

	[Test]
	public void ReadCrashLogAtReturnsNullWhenLogFileEmpty()
	{
		using var temp = new TempContainer();
		var logPath = temp.WriteCrashLog(string.Empty);

		_ = IosPlatformConfig.ReadCrashLogAt(temp.Path).Should().BeNull();
		_ = File.Exists(logPath).Should().BeFalse("the log file is consumed even when empty");
	}

	[Test]
	public void ReadCrashLogAtReturnsNullWhenLogFileWhitespace()
	{
		using var temp = new TempContainer();
		var logPath = temp.WriteCrashLog("   \n\t  \n");

		_ = IosPlatformConfig.ReadCrashLogAt(temp.Path).Should().BeNull();
		_ = File.Exists(logPath).Should().BeFalse();
	}

	[Test]
	public void ReadCrashLogAtReturnsTrimmedContentAndDeletesFile()
	{
		using var temp = new TempContainer();
		var logPath = temp.WriteCrashLog("\n  fatal: NRE at Foo.Bar  \n");

		var result = IosPlatformConfig.ReadCrashLogAt(temp.Path);

		_ = result.Should().Be("fatal: NRE at Foo.Bar");
		_ = File.Exists(logPath).Should().BeFalse("the log is removed after being read");
	}

	private const string SimctlList = """
		{
		  "devices" : {
		    "com.apple.CoreSimulator.SimRuntime.iOS-26-0" : [
		      { "udid" : "AAAA", "name" : "iPhone 17 Pro", "state" : "Shutdown", "isAvailable" : true,
		        "deviceTypeIdentifier" : "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro" }
		    ],
		    "com.apple.CoreSimulator.SimRuntime.iOS-26-2" : [
		      { "udid" : "BBBB", "name" : "iPhone 17 Pro", "state" : "Booted", "isAvailable" : true,
		        "deviceTypeIdentifier" : "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro" },
		      { "udid" : "CCCC", "name" : "iPhone 17 Pro · worker 2", "state" : "Shutdown", "isAvailable" : true,
		        "deviceTypeIdentifier" : "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro" },
		      { "udid" : "DDDD", "name" : "Broken", "state" : "Shutdown", "isAvailable" : false,
		        "deviceTypeIdentifier" : "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro" }
		    ]
		  }
		}
		""";

	[Test]
	public void ParseSimulatorRuntimePicksTheRuntimeMatchingThePlatformVersion()
	{
		var runtime = IosPlatformConfig.ParseSimulatorRuntime(SimctlList, "26.2");

		_ = runtime.Should().NotBeNull();
		_ = runtime!.Id.Should().Be("com.apple.CoreSimulator.SimRuntime.iOS-26-2");
		_ = runtime.ByName["iPhone 17 Pro"].Should().Be(
			new IosPlatformConfig.Simulator("BBBB", "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro")
		);
		_ = runtime.ByName["iPhone 17 Pro · worker 2"].Udid.Should().Be("CCCC");
	}

	[Test]
	public void ParseSimulatorRuntimeSkipsUnavailableDevices() =>
		_ = IosPlatformConfig.ParseSimulatorRuntime(SimctlList, "26.2")!.ByName.Should().NotContainKey("Broken");

	[Test]
	public void ParseSimulatorRuntimeReturnsNullForAnAbsentRuntime() =>
		_ = IosPlatformConfig.ParseSimulatorRuntime(SimctlList, "18.0").Should().BeNull();

	[Test]
	public void WorkerSimulatorNameDerivesFromTheBaseDevice() =>
		_ = IosPlatformConfig.WorkerSimulatorName("iPhone 17 Pro", 2).Should().Be("iPhone 17 Pro · worker 2");

	private sealed class TempContainer : IDisposable
	{
		public string Path { get; } = Directory.CreateTempSubdirectory("ios_container_").FullName;

		public string WriteCrashLog(string content)
		{
			var tmp = System.IO.Path.Combine(Path, "tmp");
			_ = Directory.CreateDirectory(tmp);
			var logPath = System.IO.Path.Combine(tmp, "crash.log");
			File.WriteAllText(logPath, content);
			return logPath;
		}

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
	}
}
