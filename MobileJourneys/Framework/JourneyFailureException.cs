namespace MobileJourneys.Framework;

internal sealed class JourneyFailureException : Exception
{
	public JourneyFailureException() { }

	public JourneyFailureException(string message)
		: base(message) { }

	public JourneyFailureException(string message, Exception innerException)
		: base(message, innerException) { }
}
