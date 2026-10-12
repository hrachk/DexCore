using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dex.BlazorUI.Models;

namespace Dex.BlazorUI.Services;

public sealed class OrderApiService
{
    private readonly HttpClient _http;
    private readonly ILogger<OrderApiService> _logger;

    public OrderApiService(HttpClient http, ILogger<OrderApiService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        long accountId,
        long clientOrderId,
        int marketId,
        OrderSide side,
        OrderType type,
        TimeInForce tif,
        decimal price,
        decimal quantity,
        bool reduceOnly,
        bool postOnly,
        string? apiKey,
        CancellationToken ct = default)
    {
        var body = new
        {
            accountId,
            clientOrderId,
            marketId,
            side = side.ToString(),
            type = type.ToString(),
            timeInForce = tif.ToString().ToUpperInvariant(),
            price,
            quantity,
            reduceOnly,
            postOnly,
            apiKey
        };

        try
        {
            var res = await _http.PostAsJsonAsync("/api/v1/order", body, ct);
            var json = await res.Content.ReadFromJsonAsync<PlaceOrderResponse>(ct);
            if (!res.IsSuccessStatusCode)
            {
                return new PlaceOrderResult(false, 0, OrderStatus.Rejected, json?.Error ?? "Rejected");
            }
            return new PlaceOrderResult(
                true,
                json?.OrderId ?? 0,
                Enum.TryParse<OrderStatus>(json?.Status, true, out var st) ? st : OrderStatus.New,
                "Accepted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Order API offline — returning demo acceptance");
            return new PlaceOrderResult(true, DateTime.UtcNow.Ticks, OrderStatus.New, "Demo accepted (API offline)");
        }
    }

    private sealed class PlaceOrderResponse
    {
        [JsonPropertyName("orderId")] public long OrderId { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("remaining")] public decimal Remaining { get; set; }
    }
}

public readonly record struct PlaceOrderResult(bool Success, long OrderId, OrderStatus Status, string Message);
