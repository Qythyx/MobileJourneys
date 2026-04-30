using System.Drawing;
using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class SimulatorHelperTests
{
	[Test]
	public void GetDictByKeyReturnsNestedDictionary()
	{
		var nested = new Dictionary<string, object> { ["x"] = 1 };
		var parent = new Dictionary<string, object> { ["child"] = nested };

		_ = parent.GetDict("child").Should().BeSameAs(nested);
	}

	[Test]
	public void GetDictByKeyThrowsWhenValueIsNotDictionary()
	{
		var parent = new Dictionary<string, object> { ["child"] = "not-a-dict" };

		_ = FluentActions.Invoking(() => parent.GetDict("child")).Should().Throw<InvalidCastException>();
	}

	[Test]
	public void GetDictByKeyThrowsWhenKeyMissing()
	{
		var parent = new Dictionary<string, object>();

		_ = FluentActions.Invoking(() => parent.GetDict("missing")).Should().Throw<KeyNotFoundException>();
	}

	[Test]
	public void GetIntReadsIntValue()
	{
		var parent = new Dictionary<string, object> { ["n"] = 42 };

		_ = parent.GetInt("n").Should().Be(42);
	}

	[Test]
	public void GetIntReadsLongValue()
	{
		var parent = new Dictionary<string, object> { ["n"] = 42L };

		_ = parent.GetInt("n").Should().Be(42);
	}

	[Test]
	public void GetIntReadsDoubleValueByTruncating()
	{
		// Convert.ToInt32 rounds-to-even for doubles; document expected behavior.
		var parent = new Dictionary<string, object> { ["n"] = 42.0 };

		_ = parent.GetInt("n").Should().Be(42);
	}

	[Test]
	public void GetIntParsesInvariantNumericString()
	{
		var parent = new Dictionary<string, object> { ["n"] = "42" };

		_ = parent.GetInt("n").Should().Be(42);
	}

	[Test]
	public void GetIntRejectsCultureSensitiveNumericString()
	{
		// "3,14" parses as 314 in de-DE (comma as thousand sep) but must throw under
		// InvariantCulture, otherwise Appium results would be misread on non-US machines.
		var parent = new Dictionary<string, object> { ["n"] = "3,14" };

		_ = FluentActions.Invoking(() => parent.GetInt("n")).Should().Throw<FormatException>();
	}

	[Test]
	public void GetRectangleReadsBoundsKeysInCorrectOrder()
	{
		var parent = new Dictionary<string, object>
		{
			["left"] = 10,
			["top"] = 20,
			["width"] = 100,
			["height"] = 200,
		};

		_ = parent.GetRectangle().Should().Be(new Rectangle(10, 20, 100, 200));
	}

	[Test]
	public void GetRectangleDistinguishesAllFourKeys()
	{
		// Guards against a copy-paste swap between left/top/width/height.
		var parent = new Dictionary<string, object>
		{
			["left"] = 1,
			["top"] = 2,
			["width"] = 3,
			["height"] = 4,
		};

		var rect = parent.GetRectangle();

		_ = rect.X.Should().Be(1);
		_ = rect.Y.Should().Be(2);
		_ = rect.Width.Should().Be(3);
		_ = rect.Height.Should().Be(4);
	}

	[Test]
	public void GetRectangleThrowsWhenBoundsKeyMissing()
	{
		var parent = new Dictionary<string, object>
		{
			["left"] = 0,
			["top"] = 0,
			["width"] = 0,
		};

		_ = FluentActions.Invoking(parent.GetRectangle).Should().Throw<KeyNotFoundException>();
	}
}
