using TradeDesktop.App.Commands;
using TradeDesktop.App.Helpers;
using TradeDesktop.App.State;
using TradeDesktop.Application.Services;

namespace TradeDesktop.App.ViewModels;

public sealed class ConfigViewModel : ObservableObject
{
    private readonly RuntimeConfigState _runtimeConfigState;
    private readonly IConfigService _configService;
    private string _machineHostName = string.Empty;

    private string _mapName1 = string.Empty;
    private string _mapName2 = string.Empty;

    private string _loadStatus = "Đang tải theo host name máy...";
    private string _map1CheckStatus = "Chưa kiểm tra";
    private string _map2CheckStatus = "Chưa kiểm tra";
    private string _errorMessage = string.Empty;

    private bool _isMap1Valid;
    private bool _isMap2Valid;
    private bool _isExistingRecordLoaded;
    private bool _areMapNamesEnabled;
    private bool _canSave;

    public ConfigViewModel(RuntimeConfigState runtimeConfigState, IConfigService configService)
    {
        _runtimeConfigState = runtimeConfigState;
        _configService = configService;

        CheckMap1Command = new AsyncRelayCommand(CheckMap1Async, CanCheckMap1);
        CheckMap2Command = new AsyncRelayCommand(CheckMap2Async, CanCheckMap2);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSaveCommand);
        CancelCommand = new AsyncRelayCommand(CancelAsync);

        MachineHostName = runtimeConfigState.CurrentMachineHostName;
        MapName1 = runtimeConfigState.CurrentMapName1;
        MapName2 = runtimeConfigState.CurrentMapName2;

        var hasRuntimeState =
            !string.IsNullOrWhiteSpace(MachineHostName) ||
            !string.IsNullOrWhiteSpace(MapName1) ||
            !string.IsNullOrWhiteSpace(MapName2);

        IsExistingRecordLoaded = hasRuntimeState;
        AreMapNamesEnabled = hasRuntimeState;
        LoadStatus = hasRuntimeState
            ? "✔ Đã nạp dữ liệu runtime"
            : "Đang tải theo host name máy...";

        RefreshDerivedState();

        _ = LoadByMachineHostNameAsync();
    }

    public event Action<bool?>? RequestClose;

    public string MachineHostName
    {
        get => _machineHostName;
        private set => SetProperty(ref _machineHostName, value);
    }

    public string MapName1
    {
        get => _mapName1;
        set
        {
            if (!SetProperty(ref _mapName1, value))
            {
                return;
            }

            IsMapName1Valid = false;
            Map1CheckStatus = "Chưa kiểm tra";
            RefreshDerivedState();
            RefreshButtons();
        }
    }

    public string MapName2
    {
        get => _mapName2;
        set
        {
            if (!SetProperty(ref _mapName2, value))
            {
                return;
            }

            IsMapName2Valid = false;
            Map2CheckStatus = "Chưa kiểm tra";
            RefreshDerivedState();
            RefreshButtons();
        }
    }

    public string LoadStatus
    {
        get => _loadStatus;
        private set => SetProperty(ref _loadStatus, value);
    }

    public string Map1CheckStatus
    {
        get => _map1CheckStatus;
        private set => SetProperty(ref _map1CheckStatus, value);
    }

    public string Map2CheckStatus
    {
        get => _map2CheckStatus;
        private set => SetProperty(ref _map2CheckStatus, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsMapName1Valid
    {
        get => _isMap1Valid;
        private set => SetProperty(ref _isMap1Valid, value);
    }

    public bool IsMapName2Valid
    {
        get => _isMap2Valid;
        private set => SetProperty(ref _isMap2Valid, value);
    }

    public bool IsExistingRecordLoaded
    {
        get => _isExistingRecordLoaded;
        private set => SetProperty(ref _isExistingRecordLoaded, value);
    }

    public bool AreMapNamesEnabled
    {
        get => _areMapNamesEnabled;
        private set => SetProperty(ref _areMapNamesEnabled, value);
    }

    public bool CanSave
    {
        get => _canSave;
        private set => SetProperty(ref _canSave, value);
    }

    public AsyncRelayCommand CheckMap1Command { get; }
    public AsyncRelayCommand CheckMap2Command { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }

    private bool CanCheckMap1() => AreMapNamesEnabled && !string.IsNullOrWhiteSpace(MapName1);
    private bool CanCheckMap2() => AreMapNamesEnabled && !string.IsNullOrWhiteSpace(MapName2);

    private bool CanSaveCommand() =>
        CanSave &&
        !string.IsNullOrWhiteSpace(MapName1) &&
        !string.IsNullOrWhiteSpace(MapName2);

    private async Task LoadByMachineHostNameAsync()
    {
        try
        {
            ClearError();
            var loadResult = await _configService.LoadByMachineHostNameAsync();
            MachineHostName = loadResult.MachineHostName;

            if (!loadResult.Exists)
            {
                IsExistingRecordLoaded = false;
                AreMapNamesEnabled = false;
                MapName1 = string.Empty;
                MapName2 = string.Empty;
                LoadStatus = $"✖ Không có config cho host name: {MachineHostName}";
                ErrorMessage = "Không tìm thấy record config theo host name máy hiện tại.";
                RefreshDerivedState();
                return;
            }

            if (!loadResult.IsSuccess)
            {
                IsExistingRecordLoaded = false;
                AreMapNamesEnabled = false;
                LoadStatus = "✖ Không tải được config";
                if (!string.IsNullOrWhiteSpace(loadResult.Error))
                {
                    ErrorMessage = loadResult.Error;
                }
                RefreshDerivedState();
                return;
            }

            MapName1 = loadResult.MapName1;
            MapName2 = loadResult.MapName2;
            _runtimeConfigState.Update(
                loadResult.MachineHostName,
                loadResult.MapName1,
                loadResult.MapName2,
                loadResult.Point,
                loadResult.OpenPts,
                loadResult.ConfirmGapPts,
                loadResult.HoldConfirmMs,
                loadResult.ClosePts,
                loadResult.CloseConfirmGapPts,
                loadResult.CloseHoldConfirmMs,
                loadResult.StartTimeHold,
                loadResult.EndTimeHold,
                loadResult.StartWaitTime,
                loadResult.EndWaitTime);
            IsExistingRecordLoaded = true;
            AreMapNamesEnabled = true;

            IsMapName1Valid = false;
            IsMapName2Valid = false;
            Map1CheckStatus = "Chưa kiểm tra";
            Map2CheckStatus = "Chưa kiểm tra";
            LoadStatus = "✔ Đã tải config theo host name";
            RefreshDerivedState();
        }
        catch (Exception ex)
        {
            IsExistingRecordLoaded = false;
            AreMapNamesEnabled = false;
            LoadStatus = "✖ Không tải được config";
            ErrorMessage = $"Lỗi load config theo host name: {GetErrorMessage(ex)}";
            RefreshDerivedState();
        }

        RefreshButtons();
    }

    private Task CheckMap1Async()
    {
        ClearError();
        IsMapName1Valid = SharedMemoryChecker.MapExists(MapName1.Trim());
        Map1CheckStatus = IsMapName1Valid ? "✔ Map tồn tại" : "✖ Map không tồn tại";
        RefreshDerivedState();
        RefreshButtons();
        return Task.CompletedTask;
    }

    private Task CheckMap2Async()
    {
        ClearError();
        IsMapName2Valid = SharedMemoryChecker.MapExists(MapName2.Trim());
        Map2CheckStatus = IsMapName2Valid ? "✔ Map tồn tại" : "✖ Map không tồn tại";
        RefreshDerivedState();
        RefreshButtons();
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (!CanSaveCommand() || !IsExistingRecordLoaded)
        {
            ErrorMessage = "Không thể lưu: dữ liệu chưa hợp lệ hoặc chưa load record.";
            return;
        }

        try
        {
            ClearError();
            var saveResult = await _configService.SaveByMachineHostNameAsync(MapName1, MapName2);
            if (!saveResult.IsSuccess)
            {
                LoadStatus = "✖ Save thất bại";
                ErrorMessage = string.IsNullOrWhiteSpace(saveResult.Error)
                    ? "Lưu thất bại: không có bản ghi nào được cập nhật."
                    : saveResult.Error;
                return;
            }

            if (!string.IsNullOrWhiteSpace(saveResult.MachineHostName))
            {
                MachineHostName = saveResult.MachineHostName;
            }

            LoadStatus = "✔ Lưu thành công";
            _runtimeConfigState.Update(MachineHostName, MapName1, MapName2, _runtimeConfigState.CurrentPoint);
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            LoadStatus = "✖ Save thất bại";
            ErrorMessage = $"Lỗi khi save: {GetErrorMessage(ex)}";
        }
    }

    private Task CancelAsync()
    {
        RequestClose?.Invoke(false);
        return Task.CompletedTask;
    }

    private void RefreshDerivedState()
    {
        CanSave =
            IsExistingRecordLoaded &&
            !string.IsNullOrWhiteSpace(MapName1) &&
            !string.IsNullOrWhiteSpace(MapName2);
    }

    private void RefreshButtons()
    {
        RefreshDerivedState();
        CheckMap1Command?.RaiseCanExecuteChanged();
        CheckMap2Command?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
    }

    private void ClearError() => ErrorMessage = string.Empty;

    private static string GetErrorMessage(Exception ex)
    {
        var message = ex.Message;
        if (ex.InnerException is not null)
        {
            message = $"{message} | Inner: {ex.InnerException.Message}";
        }

        return message;
    }
}
