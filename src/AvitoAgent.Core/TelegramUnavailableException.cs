namespace AvitoAgent.Core;

public sealed class TelegramUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
