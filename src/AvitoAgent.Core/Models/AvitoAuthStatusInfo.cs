namespace AvitoAgent.Core.Models;

public enum AvitoAuthState
{
    Disabled,
    Authenticated,
    NotAuthenticated,
    LoginInProgress,
    AwaitingSms,
}

public sealed record AvitoAuthStatusInfo(AvitoAuthState State, string Message);
