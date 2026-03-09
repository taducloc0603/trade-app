using System.Globalization;
using System.IO.MemoryMappedFiles;
using TradeDesktop.App.Commands;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Services;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly IMarketDataReader _marketDataReader;
    private readonly IDashboardService _dashboardService;
    private readonly IConfigRepository _configRepository;

    private string _connectionStatus = "Disconnected";
    private string _bid = "-";
    private string _ask = "-";
    private string _spread = "-";
    private string _timestamp = "-";
    private string _signal = SignalType.Hold.ToString();
    private string _reason = "Not started";

    private string _exchange1MapName = string.Empty;
    private string _exchange2MapName = string.Empty;
    private string _configCode = string.Empty;
    private string _exchange1CheckStatus = "Chưa kiểm tra";
    private string _exchange2CheckStatus = "Chưa kiểm tra";
    private string _configCodeCheckStatus = "Chưa kiểm tra";
    private string _updateStatus = "Chưa update";
    private bool _exchange1CheckSuccess;
    private bool _exchange2CheckSuccess;
    private bool _configCodeCheckSuccess;

    public DashboardViewModel(
        IMarketDataReader marketDataReader,
        IDashboardService dashboardService,
        IConfigRepository configRepository)
    {
        _marketDataReader = marketDataReader;
        _dashboardService = dashboardService;
        _configRepository = configRepository;

        StartCommand = new AsyncRelayCommand(StartAsync, () => !_marketDataReader.IsRunning);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _marketDataReader.IsRunning);
        CheckConfigCodeCommand = new AsyncRelayCommand(CheckConfigCodeAsync, CanCheckConfigCode);
        CheckExchange1MapNameCommand = new AsyncRelayCommand(CheckExchange1MapNameAsync, CanCheckExchange1MapName);
        CheckExchange2MapNameCommand = new AsyncRelayCommand(CheckExchange2MapNameAsync, CanCheckExchange2MapName);
        UpdateSansCommand = new AsyncRelayCommand(UpdateSansAsync, CanUpdateSans);

        _marketDataReader.MarketDataReceived += OnMarketDataReceived;
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string Bid
    {
        get => _bid;
        private set => SetProperty(ref _bid, value);
    }

    public string Ask
    {
        get => _ask;
        private set => SetProperty(ref _ask, value);
    }

    public string Spread
    {
        get => _spread;
        private set => SetProperty(ref _spread, value);
    }

    public string Timestamp
    {
        get => _timestamp;
        private set => SetProperty(ref _timestamp, value);
    }

    public string Signal
    {
        get => _signal;
        private set => SetProperty(ref _signal, value);
    }

    public string Reason
    {
        get => _reason;
        private set => SetProperty(ref _reason, value);
    }

    public string Exchange1MapName
    {
        get => _exchange1MapName;
        set
        {
            if (!SetProperty(ref _exchange1MapName, value))
            {
                return;
            }

            ResetExchange1Check();
            ResetUpdateStatus();
            RefreshExchangeButtons();
        }
    }

    public string Exchange2MapName
    {
        get => _exchange2MapName;
        set
        {
            if (!SetProperty(ref _exchange2MapName, value))
            {
                return;
            }

            ResetExchange2Check();
            ResetUpdateStatus();
            RefreshExchangeButtons();
        }
    }

    public string ConfigCode
    {
        get => _configCode;
        set
        {
            if (!SetProperty(ref _configCode, value))
            {
                return;
            }

            ResetConfigCodeCheck();
            ResetUpdateStatus();
            RefreshExchangeButtons();
        }
    }

    public string Exchange1CheckStatus
    {
        get => _exchange1CheckStatus;
        private set => SetProperty(ref _exchange1CheckStatus, value);
    }

    public string Exchange2CheckStatus
    {
        get => _exchange2CheckStatus;
        private set => SetProperty(ref _exchange2CheckStatus, value);
    }

    public string ConfigCodeCheckStatus
    {
        get => _configCodeCheckStatus;
        private set => SetProperty(ref _configCodeCheckStatus, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public bool Exchange1CheckSuccess
    {
        get => _exchange1CheckSuccess;
        private set => SetProperty(ref _exchange1CheckSuccess, value);
    }

    public bool Exchange2CheckSuccess
    {
        get => _exchange2CheckSuccess;
        private set => SetProperty(ref _exchange2CheckSuccess, value);
    }

    public bool ConfigCodeCheckSuccess
    {
        get => _configCodeCheckSuccess;
        private set => SetProperty(ref _configCodeCheckSuccess, value);
    }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand CheckConfigCodeCommand { get; }
    public AsyncRelayCommand CheckExchange1MapNameCommand { get; }
    public AsyncRelayCommand CheckExchange2MapNameCommand { get; }
    public AsyncRelayCommand UpdateSansCommand { get; }

    private async Task StartAsync()
    {
        await _marketDataReader.StartAsync();
        ConnectionStatus = "Connected";
        Reason = "Streaming market data";
        RefreshButtons();
    }

    private async Task StopAsync()
    {
        await _marketDataReader.StopAsync();
        ConnectionStatus = "Disconnected";
        Reason = "Stopped by user";
        RefreshButtons();
    }

    private void OnMarketDataReceived(object? sender, MarketData marketData)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            ConnectionStatus = marketData.IsConnected ? "Connected" : "Disconnected";
            Bid = marketData.Bid.ToString("F5", CultureInfo.InvariantCulture);
            Ask = marketData.Ask.ToString("F5", CultureInfo.InvariantCulture);
            Spread = marketData.Spread.ToString("F5", CultureInfo.InvariantCulture);
            Timestamp = marketData.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            var signalResult = _dashboardService.EvaluateSignal(marketData);
            Signal = signalResult.Signal.ToString();
            Reason = signalResult.Reason;
        });
    }

    private void RefreshButtons()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RefreshExchangeButtons();
    }

    private bool CanCheckConfigCode() => !string.IsNullOrWhiteSpace(ConfigCode);

    private bool CanCheckExchange1MapName() => !string.IsNullOrWhiteSpace(Exchange1MapName);

    private bool CanCheckExchange2MapName() => !string.IsNullOrWhiteSpace(Exchange2MapName);

    private bool CanUpdateSans() =>
        ConfigCodeCheckSuccess &&
        Exchange1CheckSuccess &&
        Exchange2CheckSuccess;

    private async Task CheckConfigCodeAsync()
    {
        var exists = await _configRepository.ExistsByIdAsync(ConfigCode.Trim());
        ConfigCodeCheckSuccess = exists;
        ConfigCodeCheckStatus = exists
            ? "Code hợp lệ: tồn tại trong DB"
            : "Code không hợp lệ: không tồn tại trong DB";
        ResetUpdateStatus();
        RefreshExchangeButtons();
    }

    private Task CheckExchange1MapNameAsync()
    {
        Exchange1CheckSuccess = MapNameExistsInSharedMemory(Exchange1MapName);
        Exchange1CheckStatus = Exchange1CheckSuccess
            ? "MapName tồn tại trong shared memory"
            : "Không tìm thấy MapName trong shared memory";
        ResetUpdateStatus();
        RefreshExchangeButtons();

        return Task.CompletedTask;
    }

    private Task CheckExchange2MapNameAsync()
    {
        Exchange2CheckSuccess = MapNameExistsInSharedMemory(Exchange2MapName);
        Exchange2CheckStatus = Exchange2CheckSuccess
            ? "MapName tồn tại trong shared memory"
            : "Không tìm thấy MapName trong shared memory";
        ResetUpdateStatus();
        RefreshExchangeButtons();

        return Task.CompletedTask;
    }

    private async Task UpdateSansAsync()
    {
        var updated = await _configRepository.UpdateSansAsync(
            ConfigCode.Trim(),
            Exchange1MapName.Trim(),
            Exchange2MapName.Trim());

        UpdateStatus = updated
            ? "Update thành công sans = [map1, map2] lên Supabase"
            : "Update thất bại. Kiểm tra key/quyền/table hoặc thử lại.";
    }

    private static bool MapNameExistsInSharedMemory(string? mapName)
    {
        var normalized = mapName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        try
        {
            using var _ = MemoryMappedFile.OpenExisting(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ResetExchange1Check()
    {
        Exchange1CheckSuccess = false;
        Exchange1CheckStatus = "Chưa kiểm tra";
    }

    private void ResetExchange2Check()
    {
        Exchange2CheckSuccess = false;
        Exchange2CheckStatus = "Chưa kiểm tra";
    }

    private void ResetConfigCodeCheck()
    {
        ConfigCodeCheckSuccess = false;
        ConfigCodeCheckStatus = "Chưa kiểm tra";
    }

    private void ResetUpdateStatus()
    {
        UpdateStatus = "Chưa update";
    }

    private void RefreshExchangeButtons()
    {
        CheckConfigCodeCommand.RaiseCanExecuteChanged();
        CheckExchange1MapNameCommand.RaiseCanExecuteChanged();
        CheckExchange2MapNameCommand.RaiseCanExecuteChanged();
        UpdateSansCommand.RaiseCanExecuteChanged();
    }
}