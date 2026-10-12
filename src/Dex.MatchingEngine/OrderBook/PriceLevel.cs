using System.Runtime.CompilerServices;
using Dex.Domain.Orders;

namespace Dex.MatchingEngine.OrderBook;

/// <summary>
/// Single price level holding a FIFO queue of orders at the same price.
/// Uses LinkedList for O(1) insert/remove while preserving time priority.
/// In production this can be replaced by a pooled array-based structure for further cache locality.
/// </summary>
internal sealed class PriceLevel
{
    private readonly LinkedList<Order> _orders = new();
    private decimal _totalQuantity;

    public decimal Price { get; }
    public decimal TotalQuantity => _totalQuantity;
    public int OrderCount => _orders.Count;
    public bool IsEmpty => _orders.Count == 0;

    public PriceLevel(decimal price)
    {
        if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price));
        Price = price;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddOrder(Order order)
    {
        _orders.AddLast(order);
        _totalQuantity += order.RemainingQuantity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRemoveOrder(long orderId, out Order? removed)
    {
        var node = _orders.First;
        while (node is not null)
        {
            if (node.Value.OrderId == orderId)
            {
                removed = node.Value;
                _totalQuantity -= removed.RemainingQuantity;
                _orders.Remove(node);
                return true;
            }
            node = node.Next;
        }

        removed = null;
        return false;
    }

    /// <summary>
    /// Walks the FIFO queue and consumes quantity at this price level.
    /// Returns list of fills (maker order + filled qty).
    /// </summary>
    public List<(Order Maker, decimal FillQty)> Match(decimal quantityToFill)
    {
        var fills = new List<(Order, decimal)>(4);
        var remaining = quantityToFill;

        while (remaining > 0 && _orders.First is { } node)
        {
            var maker = node.Value;
            var available = maker.RemainingQuantity;
            var fillQty = Math.Min(available, remaining);

            maker.ApplyFill(fillQty);
            _totalQuantity -= fillQty;
            remaining -= fillQty;

            fills.Add((maker, fillQty));

            if (maker.RemainingQuantity == 0)
            {
                _orders.RemoveFirst();
            }
        }

        return fills;
    }

    public IEnumerable<Order> GetOrders() => _orders;
}
