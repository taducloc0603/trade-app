using System.Collections.ObjectModel;

namespace TradeDesktop.App.ViewModels;

public enum OrderRecordLayoutMode
{
    Summary,
    TradeTable,
    HistoryTable
}

public sealed class OrderPanelStatusViewModel : ObservableObject
{
    private string _panelTitle;
    private string _sourceTickMapName;
    private string _targetMapName;
    private bool _isLoading;
    private bool _isMapAvailable;
    private bool _hasError;
    private bool _isEmpty;
    private string _statusMessage;
    private readonly OrderRecordLayoutMode _recordLayoutMode;

    public OrderPanelStatusViewModel(
        string panelTitle,
        OrderRecordLayoutMode recordLayoutMode = OrderRecordLayoutMode.Summary)
    {
        _panelTitle = panelTitle;
        _sourceTickMapName = string.Empty;
        _targetMapName = string.Empty;
        _statusMessage = "Đang tải dữ liệu...";
        _recordLayoutMode = recordLayoutMode;

        Records = [];
        LeftItems = [];
        RightItems = [];
    }

    public string PanelTitle
    {
        get => _panelTitle;
        set => SetProperty(ref _panelTitle, value);
    }

    public string SourceTickMapName
    {
        get => _sourceTickMapName;
        set => SetProperty(ref _sourceTickMapName, value);
    }

    public string TargetMapName
    {
        get => _targetMapName;
        set => SetProperty(ref _targetMapName, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsMapAvailable
    {
        get => _isMapAvailable;
        private set => SetProperty(ref _isMapAvailable, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool UseSummaryRecords => _recordLayoutMode == OrderRecordLayoutMode.Summary;
    public bool UseTradeStructuredColumns => _recordLayoutMode == OrderRecordLayoutMode.TradeTable;
    public bool UseHistoryStructuredColumns => _recordLayoutMode == OrderRecordLayoutMode.HistoryTable;

    public ObservableCollection<OrderRecordItemViewModel> Records { get; }
    public ObservableCollection<OrderInfoFieldViewModel> LeftItems { get; }
    public ObservableCollection<OrderInfoFieldViewModel> RightItems { get; }

    public void ApplyMapBinding(string sourceTickMapName, string targetMapName)
    {
        SourceTickMapName = sourceTickMapName ?? string.Empty;
        TargetMapName = targetMapName ?? string.Empty;
    }

    public void SetLoading()
    {
        IsLoading = true;
        IsMapAvailable = false;
        HasError = false;
        IsEmpty = false;
        StatusMessage = "Đang tải dữ liệu...";
        ReplaceRecords(Array.Empty<OrderRecordItemViewModel>());
        ReplaceFields(Array.Empty<OrderInfoFieldViewModel>(), Array.Empty<OrderInfoFieldViewModel>());
    }

    public void SetMapNotFound(string mapName)
    {
        IsLoading = false;
        IsMapAvailable = false;
        HasError = true;
        IsEmpty = false;
        StatusMessage = $"Không tìm thấy map: {mapName}";
        ReplaceRecords(Array.Empty<OrderRecordItemViewModel>());
        ReplaceFields(Array.Empty<OrderInfoFieldViewModel>(), Array.Empty<OrderInfoFieldViewModel>());
    }

    public void SetParseError(string message)
    {
        IsLoading = false;
        IsMapAvailable = true;
        HasError = true;
        IsEmpty = false;
        StatusMessage = string.IsNullOrWhiteSpace(message) ? "Lỗi parse dữ liệu" : message;
        ReplaceRecords(Array.Empty<OrderRecordItemViewModel>());
        ReplaceFields(Array.Empty<OrderInfoFieldViewModel>(), Array.Empty<OrderInfoFieldViewModel>());
    }

    public void SetEmpty()
    {
        IsLoading = false;
        IsMapAvailable = true;
        HasError = false;
        IsEmpty = true;
        StatusMessage = "Chưa có dữ liệu";
        ReplaceRecords(Array.Empty<OrderRecordItemViewModel>());
        ReplaceFields(Array.Empty<OrderInfoFieldViewModel>(), Array.Empty<OrderInfoFieldViewModel>());
    }

    public void SetData(
        IEnumerable<OrderRecordItemViewModel> records,
        IEnumerable<OrderInfoFieldViewModel> leftFields,
        IEnumerable<OrderInfoFieldViewModel> rightFields)
    {
        IsLoading = false;
        IsMapAvailable = true;
        HasError = false;
        IsEmpty = false;
        StatusMessage = "Đã kết nối";
        ReplaceRecords(records);
        ReplaceFields(leftFields, rightFields);
    }

    private void ReplaceRecords(IEnumerable<OrderRecordItemViewModel> records)
    {
        Records.Clear();
        foreach (var record in records)
        {
            Records.Add(record);
        }
    }

    private void ReplaceFields(
        IEnumerable<OrderInfoFieldViewModel> leftFields,
        IEnumerable<OrderInfoFieldViewModel> rightFields)
    {
        LeftItems.Clear();
        foreach (var item in leftFields)
        {
            LeftItems.Add(item);
        }

        RightItems.Clear();
        foreach (var item in rightFields)
        {
            RightItems.Add(item);
        }
    }
}