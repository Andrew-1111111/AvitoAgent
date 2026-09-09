namespace AvitoAgent.Core;

/// <summary>
/// LM Studio не запущена, сервер недоступен или модель не загружена.
/// </summary>
public sealed class LmStudioUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
