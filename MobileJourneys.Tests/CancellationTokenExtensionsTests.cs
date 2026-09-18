using System.Diagnostics;
using AwesomeAssertions;
using NUnit.Framework;

namespace MobileJourneys.Tests;

[TestFixture]
public sealed class CancellationTokenExtensionsTests
{
	[Test]
	public void SleepThrowsAsSoonAsTheTokenIsCancelled()
	{
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
		var stopwatch = Stopwatch.StartNew();

		_ = FluentActions
			.Invoking(() => cancellation.Token.Sleep(TimeSpan.FromMinutes(1)))
			.Should()
			.Throw<OperationCanceledException>();
		_ = stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
	}
}
