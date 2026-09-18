namespace MobileJourneys;

/// <summary>Blocking waits that end as soon as the run is interrupted.</summary>
internal static class CancellationTokenExtensions
{
	/// <summary>Blocks for <paramref name="duration"/>, or until the token is cancelled.</summary>
	/// <param name="cancellationToken">The token that ends the wait early.</param>
	/// <param name="duration">How long to block.</param>
	/// <exception cref="OperationCanceledException">The token was cancelled before or during the wait.</exception>
	public static void Sleep(this CancellationToken cancellationToken, TimeSpan duration)
	{
		_ = cancellationToken.WaitHandle.WaitOne(duration);
		cancellationToken.ThrowIfCancellationRequested();
	}
}
