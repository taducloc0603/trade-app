namespace TradeDesktop.App.ViewModels;

public sealed class OrderRecordItemViewModel
{
    public OrderRecordItemViewModel(string summary)
    {
        Summary = summary;
        HasStructuredColumns = false;
    }

    public string Summary { get; }

    public bool HasStructuredColumns { get; }

    public string Index { get; } = string.Empty;
    public string Symbol { get; } = string.Empty;
    public string Ticket { get; } = string.Empty;
    public string Type { get; } = string.Empty;
    public string Lot { get; } = string.Empty;
    public string Price { get; } = string.Empty;
    public string Sl { get; } = string.Empty;
    public string Tp { get; } = string.Empty;
    public string Profit { get; } = string.Empty;
    public string Time { get; } = string.Empty;
    public string TimeMsc { get; } = string.Empty;

    public OrderRecordItemViewModel(
        string index,
        string symbol,
        string ticket,
        string type,
        string lot,
        string price,
        string sl,
        string tp,
        string profit,
        string time,
        string timeMsc)
    {
        Index = index;
        Symbol = symbol;
        Ticket = ticket;
        Type = type;
        Lot = lot;
        Price = price;
        Sl = sl;
        Tp = tp;
        Profit = profit;
        Time = time;
        TimeMsc = timeMsc;
        HasStructuredColumns = true;

        Summary = string.Join(" | ",
            index,
            symbol,
            ticket,
            type,
            lot,
            price,
            sl,
            tp,
            profit,
            time,
            timeMsc);
    }
}