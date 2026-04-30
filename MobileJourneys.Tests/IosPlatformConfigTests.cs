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
