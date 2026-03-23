namespace TradeDesktop.Application.Models;

public sealed record TradeSharedRecord(
    ulong Ticket,
    double Lot,
    double Price,
    double Sl,
    double Tp,
    double Profit,
    int TradeType,
    int OpenTime);