namespace TradeDesktop.App.ViewModels;

public sealed class TradeRowViewModel
{
    public TradeRowViewModel(
        string timestamp,
        string count,
        string symbol,
        string ticket,
        string type,
        string lot,
        string price,
        string sl,
        string tp,
        string feeSpread,
        string time)
    {
        Timestamp = timestamp;
        Count = count;
        Symbol = symbol;
        Ticket = ticket;
        Type = type;
        Lot = lot;
        Price = price;
        Sl = sl;
        Tp = tp;
        FeeSpread = feeSpread;
        Time = time;
    }

    public string Timestamp { get; }
    public string Count { get; }
    public string Symbol { get; }
    public string Ticket { get; }
    public string Type { get; }
    public string Lot { get; }
    public string Price { get; }
    public string Sl { get; }
    public string Tp { get; }
    public string FeeSpread { get; }
    public string Time { get; }
}