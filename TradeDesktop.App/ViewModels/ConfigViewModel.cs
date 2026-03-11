using TradeDesktop.App.Commands;
using TradeDesktop.App.Helpers;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;

namespace TradeDesktop.App.ViewModels;

public sealed class ConfigViewModel : ObservableObject
{
    private readonly RuntimeConfigState _runtimeConfigState;
    private readonly IConfigRepository _configRepository;

    private string _code = string.Empty;
    private string _mapName1 = string.Empty;
    private string _mapName2 = string.Empty;

    private string _codeCheckStatus = "Chưa kiểm tra";
    private string _map1CheckStatus = "Chưa kiểm tra";
    private string _map2CheckStatus = "Chưa kiểm tra";

    private bool _isCodeValid;
    private bool _isMap1Valid;
    private bool _isMap2Valid;

    public ConfigViewModel(RuntimeConfigState runtimeConfigState, IConfigRepository configRepository)
    {
        _runtimeConfigState = runtimeConfigState;
        _configRepository = configRepository;

        CheckCodeCommand = new AsyncRelayCommand(CheckCodeAsync, CanCheckCode);
        CheckMap1Command = new AsyncRelayCommand(CheckMap1Async, CanCheckMap1);
        CheckMap2Command = new AsyncRelayCommand(CheckMap2Async, CanCheckMap2);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        CancelCommand = new AsyncRelayCommand(CancelAsync);

        Code = runtimeConfigState.Code;
        MapName1 = runtimeConfigState.MapName1;
        MapName2 = runtimeConfigState.MapName2;
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

            _isCodeValid = false;
            CodeCheckStatus = "Chưa kiểm tra";
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

            _isMap1Valid = false;
            Map1CheckStatus = "Chưa kiểm tra";
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

            _isMap2Valid = false;
            Map2CheckStatus = "Chưa kiểm tra";
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

    public AsyncRelayCommand CheckCodeCommand { get; }
    public AsyncRelayCommand CheckMap1Command { get; }
    public AsyncRelayCommand CheckMap2Command { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }

    private bool CanCheckCode() => !string.IsNullOrWhiteSpace(Code);
    private bool CanCheckMap1() => !string.IsNullOrWhiteSpace(MapName1);
    private bool CanCheckMap2() => !string.IsNullOrWhiteSpace(MapName2);

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(Code) &&
        !string.IsNullOrWhiteSpace(MapName1) &&
        !string.IsNullOrWhiteSpace(MapName2);

    private async Task CheckCodeAsync()
    {
        try
        {
            var exists = await _configRepository.ExistsByIdAsync(Code.Trim());
            _isCodeValid = exists;
            CodeCheckStatus = exists ? "✔ Code tồn tại" : "✖ Code không tồn tại";
        }
        catch
        {
            _isCodeValid = false;
            CodeCheckStatus = "✖ Code không tồn tại";
        }

        RefreshButtons();
    }

    private Task CheckMap1Async()
    {
        _isMap1Valid = SharedMemoryChecker.MapExists(MapName1.Trim());
        Map1CheckStatus = _isMap1Valid ? "✔ Map tồn tại" : "✖ Map không tồn tại";
        RefreshButtons();
        return Task.CompletedTask;
    }

    private Task CheckMap2Async()
    {
        _isMap2Valid = SharedMemoryChecker.MapExists(MapName2.Trim());
        Map2CheckStatus = _isMap2Valid ? "✔ Map tồn tại" : "✖ Map không tồn tại";
        RefreshButtons();
        return Task.CompletedTask;
    }

    private Task SaveAsync()
    {
        _runtimeConfigState.Update(Code, MapName1, MapName2);
        RequestClose?.Invoke(true);
        return Task.CompletedTask;
    }

    private Task CancelAsync()
    {
        RequestClose?.Invoke(false);
        return Task.CompletedTask;
    }

    private void RefreshButtons()
    {
        CheckCodeCommand?.RaiseCanExecuteChanged();
        CheckMap1Command?.RaiseCanExecuteChanged();
        CheckMap2Command?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
    }
}
