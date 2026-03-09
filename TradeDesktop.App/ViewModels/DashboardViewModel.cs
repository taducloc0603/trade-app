using System.Globalization;
using System.Windows;
using TradeDesktop.App.Commands;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Services;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly IMarketDataReader _marketDataReader;
    private readonly IDashboardService _dashboardService;

    private string _connectionStatus = "Disconnected";
    private string _bid = "-";
    private string _ask = "-";
    private string _spread = "-";
    private string _timestamp = "-";
    private string _signal = SignalType.Hold.ToString();
    private string _reason = "Not started";

    public DashboardViewModel(IMarketDataReader marketDataReader, IDashboardService dashboardService)
    {
        _marketDataReader = marketDataReader;
        _dashboardService = dashboardService;

        StartCommand = new AsyncRelayCommand(StartAsync, () => !_marketDataReader.IsRunning);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _marketDataReader.IsRunning);

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

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }

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
        Application.Current.Dispatcher.Invoke(() =>
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
    }
}