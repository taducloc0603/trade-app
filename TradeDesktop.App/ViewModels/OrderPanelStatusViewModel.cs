namespace TradeDesktop.App.ViewModels;

public sealed class OrderPanelStatusViewModel : ObservableObject
{
    private string _panelTitle;
    private string _sourceTickMapName;
    private string _targetMapName;
    private bool _isMapAvailable;
    private string _statusMessage;

    public OrderPanelStatusViewModel(string panelTitle)
    {
        _panelTitle = panelTitle;
        _sourceTickMapName = string.Empty;
        _targetMapName = string.Empty;
        _statusMessage = "Không tìm thấy map: ";
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
        set
        {
            if (!SetProperty(ref _targetMapName, value))
            {
                return;
            }

            UpdateStatusMessage();
        }
    }

    public bool IsMapAvailable
    {
        get => _isMapAvailable;
        set
        {
            if (!SetProperty(ref _isMapAvailable, value))
            {
                return;
            }

            UpdateStatusMessage();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public void ApplyMapBinding(string sourceTickMapName, string targetMapName)
    {
        SourceTickMapName = sourceTickMapName ?? string.Empty;
        TargetMapName = targetMapName ?? string.Empty;
    }

    private void UpdateStatusMessage()
    {
        StatusMessage = IsMapAvailable
            ? "Đã kết nối"
            : $"Không tìm thấy map: {TargetMapName}";
    }
}