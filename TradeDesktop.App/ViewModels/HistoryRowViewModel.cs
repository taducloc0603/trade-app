namespace TradeDesktop.App.ViewModels;

public sealed class HistoryRowViewModel
{
    public HistoryRowViewModel(
        string timestamp,
        string count,
        string symbol,
        string ticket,
        string type,
        string volume,
        string openPrice,
        string closePrice,
        string profit,
        string feeSpread,
        string commission,
        string sl,
        string tp,
        string openTime,
        string closeTime)
    {
        Timestamp = timestamp;
        Count = count;
        Symbol = symbol;
        Ticket = ticket;
        Type = type;
        Volume = volume;
        OpenPrice = openPrice;
        ClosePrice = closePrice;
        Profit = profit;
        FeeSpread = feeSpread;
        Commission = commission;
        Sl = sl;
        Tp = tp;
        OpenTime = openTime;
        CloseTime = closeTime;
    }

    public string Timestamp { get; }
    public string Count { get; }
    public string Symbol { get; }
    public string Ticket { get; }
    public string Type { get; }
    public string Volume { get; }
    public string OpenPrice { get; }
    public string ClosePrice { get; }
    public string Profit { get; }
    public string FeeSpread { get; }
    public string Commission { get; }
    public string Sl { get; }
    public string Tp { get; }
    public string OpenTime { get; }
    public string CloseTime { get; }
}