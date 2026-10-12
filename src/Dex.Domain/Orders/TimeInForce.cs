namespace Dex.Domain.Orders;

public enum TimeInForce : byte
{
    GTC = 0,   // Good Till Cancel
    IOC = 1,   // Immediate Or Cancel
    FOK = 2,   // Fill Or Kill
    GTD = 3    // Good Till Date
}
