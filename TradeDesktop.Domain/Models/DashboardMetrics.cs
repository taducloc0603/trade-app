namespace TradeDesktop.Domain.Models;

public sealed record ExchangeDashboardMetrics(
    string Symbol,
    decimal? Bid,
    decimal? Ask,
    decimal? Spread,
    decimal? LatencyMs,
    decimal? Tps,
    string Time,
    decimal? MaxLatMs,
    decimal? AvgLatMs,
    bool IsConnected,
    string? Error);

public sealed record DashboardMetrics(
    ExchangeDashboardMetrics ExchangeA,
    ExchangeDashboardMetrics ExchangeB,
    decimal? GapBuy,
    decimal? GapSell,
    bool IsConnectedA,
    bool IsConnectedB,
    DateTime TimestampUtc);
