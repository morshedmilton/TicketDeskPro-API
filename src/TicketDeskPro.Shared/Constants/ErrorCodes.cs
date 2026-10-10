namespace TicketDeskPro.Shared.Constants;

/// <summary>Stable error codes from the FRD §15 error matrix.</summary>
public static class ErrorCodes
{
    public const string InvalidCredentials = "AUTH_001";
    public const string TokenExpired = "AUTH_002";
    public const string InvalidTokenState = "AUTH_003";
    public const string MissingPermission = "AUTH_004";
    public const string UserLocked = "AUTH_005";
    public const string TicketNotFound = "RES_001";
    public const string UserNotFound = "RES_002";
    public const string Validation = "VAL_001";
    public const string FileTooLarge = "VAL_002";
    public const string InvalidFileType = "VAL_003";
    public const string EmailExists = "CPL_001";
    public const string InvalidStateTransition = "CPL_002";
    public const string InactiveAgent = "CPL_003";
    public const string ConcurrencyConflict = "CPL_004";
    public const string AuthRateLimited = "RAT_001";
    public const string ApiRateLimited = "RAT_002";
    public const string DatabaseTimeout = "SYS_001";
    public const string RedisUnavailable = "SYS_002";
    public const string SmtpFailure = "SYS_003";
    public const string Unexpected = "SYS_999";
}
