namespace TradeDesktop.Application.Models;

public sealed record ConfigRecord(
    string Id,
    string Code,
    string SansJson,
    string? Ip);