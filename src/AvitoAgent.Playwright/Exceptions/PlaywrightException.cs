namespace AvitoAgent.Playwright.Exceptions;

public sealed class PlaywrightException : Exception
{
    public PlaywrightException(string message)
        : base(message) { }

    public PlaywrightException(string message, Exception innerException)
        : base(message, innerException) { }
}
