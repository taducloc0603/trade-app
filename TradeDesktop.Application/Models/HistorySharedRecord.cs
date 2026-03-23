namespace TradeDesktop.Application.Models;

public sealed record HistorySharedRecord(
    ulong Ticket,
    double Profit,
    double Volume,
    int DealTime);