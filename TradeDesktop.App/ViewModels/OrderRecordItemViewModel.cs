namespace TradeDesktop.App.ViewModels;

public sealed class OrderRecordItemViewModel
{
    public OrderRecordItemViewModel(string summary)
    {
        Summary = summary;
    }

    public string Summary { get; }
}