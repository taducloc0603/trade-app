using TradeDesktop.App.Commands;
using TradeDesktop.App.Helpers;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;

namespace TradeDesktop.App.ViewModels;

public sealed class ConfigViewModel : ObservableObject
{
    private readonly RuntimeConfigState _runtimeConfigState;
    private readonly IConfigRepository _configRepository;
    private string _loadedCode = string.Empty;

    private string _code = string.Empty;
    private string _mapName1 = string.Empty;
    private string _mapName2 = string.Empty;

    private string _codeCheckStatus = "Chưa kiểm tra";
    private string _map1CheckStatus = "Chưa kiểm tra";
    private string _map2CheckStatus = "Chưa kiểm tra";

    private bool _isCodeValid;
    private bool _isMap1Valid;
    private bool _isMap2Valid;
    private bool _isExistingRecordLoaded;
    private bool _isCodeReadOnly;
    private bool _areMapNamesEnabled;
    private bool _canSave;
    private bool _isApplyingLoadedRecord;

    public ConfigViewModel(RuntimeConfigState runtimeConfigState, IConfigRepository configRepository)
    {
        _runtimeConfigState = runtimeConfigState;
        _configRepository = configRepository;

        CheckCodeCommand = new AsyncRelayCommand(CheckCodeAsync, CanCheckCode);
        CheckMap1Command = new AsyncRelayCommand(CheckMap1Async, CanCheckMap1);
        CheckMap2Command = new AsyncRelayCommand(CheckMap2Async, CanCheckMap2);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSaveCommand);
        CancelCommand = new AsyncRelayCommand(CancelAsync);

        Code = runtimeConfigState.Code;
        MapName1 = string.Empty;
        MapName2 = string.Empty;
        IsCodeReadOnly = false;
        AreMapNamesEnabled = false;
        RefreshDerivedState();
    }

    public event Action<bool?>? RequestClose;

    public string Code
    {
        get => _code;
        set
        {
            if (!SetProperty(ref _code, value))
            {
                return;
            }

            if (_isApplyingLoadedRecord)
            {
                RefreshButtons();
                return;
            }

            _loadedCode = string.Empty;
            IsCodeValid = false;
            IsExistingRecordLoaded = false;
            IsCodeReadOnly = false;
            AreMapNamesEnabled = false;
            CodeCheckStatus = "Chưa kiểm tra";
            MapName1 = string.Empty;
            MapName2 = string.Empty;
            RefreshButtons();
        }
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

    public string CodeCheckStatus
    {
        get => _codeCheckStatus;
        private set => SetProperty(ref _codeCheckStatus, value);
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

    public bool IsCodeValid
    {
        get => _isCodeValid;
        private set => SetProperty(ref _isCodeValid, value);
    }

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

    public bool IsCodeReadOnly
    {
        get => _isCodeReadOnly;
        private set => SetProperty(ref _isCodeReadOnly, value);
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

    public AsyncRelayCommand CheckCodeCommand { get; }
    public AsyncRelayCommand CheckMap1Command { get; }
    public AsyncRelayCommand CheckMap2Command { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }

    private bool CanCheckCode() => !string.IsNullOrWhiteSpace(Code) && !IsCodeReadOnly;
    private bool CanCheckMap1() => AreMapNamesEnabled && !string.IsNullOrWhiteSpace(MapName1);
    private bool CanCheckMap2() => AreMapNamesEnabled && !string.IsNullOrWhiteSpace(MapName2);

    private bool CanSaveCommand() =>
        CanSave &&
        !string.IsNullOrWhiteSpace(MapName1) &&
        !string.IsNullOrWhiteSpace(MapName2);

    private async Task CheckCodeAsync()
    {
        try
        {
            var inputCode = Code.Trim();
            var exists = await _configRepository.ExistsByCodeAsync(inputCode);
            IsCodeValid = exists;

            if (!exists)
            {
                IsExistingRecordLoaded = false;
                AreMapNamesEnabled = false;
                IsCodeReadOnly = false;
                MapName1 = string.Empty;
                MapName2 = string.Empty;
                CodeCheckStatus = "✖ Code không tồn tại";
                RefreshDerivedState();
                return;
            }

            var record = await _configRepository.GetByCodeAsync(inputCode);
            if (record is null)
            {
                IsCodeValid = false;
                IsExistingRecordLoaded = false;
                AreMapNamesEnabled = false;
                IsCodeReadOnly = false;
                CodeCheckStatus = "✖ Không tải được config";
                RefreshDerivedState();
                return;
            }

            var (mapName1, mapName2) = SansHelper.ParseSans(record.SansJson);

            _isApplyingLoadedRecord = true;
            try
            {
                _loadedCode = record.Code.Trim();
                Code = _loadedCode;
                MapName1 = mapName1;
                MapName2 = mapName2;
            }
            finally
            {
                _isApplyingLoadedRecord = false;
            }

            IsCodeValid = true;
            IsExistingRecordLoaded = true;
            IsCodeReadOnly = true;
            AreMapNamesEnabled = true;

            IsMapName1Valid = false;
            IsMapName2Valid = false;
            Map1CheckStatus = "Chưa kiểm tra";
            Map2CheckStatus = "Chưa kiểm tra";
            CodeCheckStatus = "✔ Code tồn tại";
            RefreshDerivedState();
        }
        catch
        {
            IsCodeValid = false;
            IsExistingRecordLoaded = false;
            AreMapNamesEnabled = false;
            IsCodeReadOnly = false;
            CodeCheckStatus = "✖ Code không tồn tại";
            RefreshDerivedState();
        }

        RefreshButtons();
    }

    private Task CheckMap1Async()
    {
        IsMapName1Valid = SharedMemoryChecker.MapExists(MapName1.Trim());
        Map1CheckStatus = IsMapName1Valid ? "✔ Map tồn tại" : "✖ Map không tồn tại";
        RefreshDerivedState();
        RefreshButtons();
        return Task.CompletedTask;
    }

    private Task CheckMap2Async()
    {
        IsMapName2Valid = SharedMemoryChecker.MapExists(MapName2.Trim());
        Map2CheckStatus = IsMapName2Valid ? "✔ Map tồn tại" : "✖ Map không tồn tại";
        RefreshDerivedState();
        RefreshButtons();
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (!CanSaveCommand() || string.IsNullOrWhiteSpace(_loadedCode))
        {
            return;
        }

        var sansJson = SansHelper.BuildSans(MapName1, MapName2);
        var ip = IpHelper.GetLocalIpAddress();

        var updated = await _configRepository.UpdateSansAndIpByCodeAsync(_loadedCode, sansJson, ip);
        if (!updated)
        {
            CodeCheckStatus = "✖ Save thất bại";
            return;
        }

        _runtimeConfigState.Update(_loadedCode, MapName1, MapName2);
        RequestClose?.Invoke(true);
    }

    private Task CancelAsync()
    {
        RequestClose?.Invoke(false);
        return Task.CompletedTask;
    }

    private void RefreshDerivedState()
    {
        CanSave =
            IsCodeValid &&
            IsExistingRecordLoaded &&
            !string.IsNullOrWhiteSpace(MapName1) &&
            !string.IsNullOrWhiteSpace(MapName2);
    }

    private void RefreshButtons()
    {
        RefreshDerivedState();
        CheckCodeCommand?.RaiseCanExecuteChanged();
        CheckMap1Command?.RaiseCanExecuteChanged();
        CheckMap2Command?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
    }
}
