namespace Dex.RiskEngine.Models;

public enum RiskRejectReason : byte
{
    None = 0,
    InsufficientMargin = 1,
    ExceedsMaxPositionSize = 2,
    ExceedsOpenInterestCap = 3,
    BelowMinNotional = 4,
    InvalidParameters = 5,
    AccountNotFound = 6,
    MarketNotFound = 7
}

public readonly struct RiskCheckResult
{
    public bool IsAccepted { get; }
    public RiskRejectReason RejectReason { get; }
    public string Message { get; }
    public decimal RequiredInitialMargin { get; }
    public decimal AvailableMargin { get; }

    public static RiskCheckResult Accept(decimal requiredIm, decimal available)
        => new(true, RiskRejectReason.None, string.Empty, requiredIm, available);

    public static RiskCheckResult Reject(RiskRejectReason reason, string message, decimal requiredIm = 0m, decimal available = 0m)
        => new(false, reason, message, requiredIm, available);

    private RiskCheckResult(bool isAccepted, RiskRejectReason reason, string message, decimal requiredIm, decimal available)
    {
        IsAccepted = isAccepted;
        RejectReason = reason;
        Message = message;
        RequiredInitialMargin = requiredIm;
        AvailableMargin = available;
    }
}
