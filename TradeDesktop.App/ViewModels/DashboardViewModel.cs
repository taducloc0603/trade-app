using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TradeDesktop.App.Commands;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RuntimeConfigState _runtimeConfigState;

    private string _runtimeSummary = string.Empty;
    private string _exchangeAHeader = "Sàn A";
    private string _exchangeBHeader = "Sàn B";
    private string _gapBuy = "0.00000";
    private string _gapSell = "0.00000";

    public DashboardViewModel(
        IServiceProvider serviceProvider,
        RuntimeConfigState runtimeConfigState,
        IMarketDataReader marketDataReader)
    {
        _serviceProvider = serviceProvider;
        _runtimeConfigState = runtimeConfigState;

        ParameterRows = new ObservableCollection<ParameterRowViewModel>
        {
            new("Symbol", "BTCUSDT", "BTCUSDT"),
            new("Bid", "100.12000", "100.11000"),
            new("Ask", "100.13000", "100.14000"),
            new("Spread", "0.01000", "0.03000"),
            new("Latency(ms)", "8", "10"),
            new("TPS", "120", "112"),
            new("Time", DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            new("Max Lat(ms)", "15", "18"),
            new("Avg Lat(ms)", "9", "11")
        };

        LogItems = new ObservableCollection<string>
        {
            "[Mock] Connected to market data stream.",
            "[Mock] UI preview mode - logs panel only.",
            "[Mock] Waiting for runtime config update..."
        };

        OpenConfigCommand = new AsyncRelayCommand(OpenConfigAsync);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);

        _runtimeConfigState.StateChanged += (_, _) => ApplyRuntimeConfig();
        ApplyRuntimeConfig();

        marketDataReader.MarketDataReceived += OnMarketDataReceived;
        _ = marketDataReader.StartAsync();
    }

    public ObservableCollection<ParameterRowViewModel> ParameterRows { get; }
    public ObservableCollection<string> LogItems { get; }

    public string RuntimeSummary
    {
        get => _runtimeSummary;
        private set => SetProperty(ref _runtimeSummary, value);
    }

    public string ExchangeAHeader
    {
        get => _exchangeAHeader;
        private set => SetProperty(ref _exchangeAHeader, value);
    }

    public string ExchangeBHeader
    {
        get => _exchangeBHeader;
        private set => SetProperty(ref _exchangeBHeader, value);
    }

    public string GapBuy
    {
        get => _gapBuy;
        private set => SetProperty(ref _gapBuy, value);
    }

    public string GapSell
    {
        get => _gapSell;
        private set => SetProperty(ref _gapSell, value);
    }

    public AsyncRelayCommand OpenConfigCommand { get; }
    public AsyncRelayCommand ClearLogsCommand { get; }

    private Task OpenConfigAsync()
    {
        var configWindow = _serviceProvider.GetRequiredService<ConfigWindow>();
        configWindow.Owner = System.Windows.Application.Current.MainWindow;
        configWindow.ShowDialog();
        return Task.CompletedTask;
    }

    private Task ClearLogsAsync()
    {
        return Task.CompletedTask;
    }

    private void ApplyRuntimeConfig()
    {
        ExchangeAHeader = string.IsNullOrWhiteSpace(_runtimeConfigState.MapName1)
            ? "Sàn A"
            : $"Sàn A ({_runtimeConfigState.MapName1})";

        ExchangeBHeader = string.IsNullOrWhiteSpace(_runtimeConfigState.MapName2)
            ? "Sàn B"
            : $"Sàn B ({_runtimeConfigState.MapName2})";

        RuntimeSummary =
            $"Code: {_runtimeConfigState.Code}  |  Map 1: {_runtimeConfigState.MapName1}  |  Map 2: {_runtimeConfigState.MapName2}";
    }

    private void OnMarketDataReceived(object? sender, MarketData marketData)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            SetRowValue("Bid", marketData.Bid.ToString("F5", CultureInfo.InvariantCulture), (marketData.Bid - 0.01m).ToString("F5", CultureInfo.InvariantCulture));
            SetRowValue("Ask", marketData.Ask.ToString("F5", CultureInfo.InvariantCulture), (marketData.Ask + 0.01m).ToString("F5", CultureInfo.InvariantCulture));
            SetRowValue("Spread", marketData.Spread.ToString("F5", CultureInfo.InvariantCulture), (marketData.Spread + 0.02m).ToString("F5", CultureInfo.InvariantCulture));
            SetRowValue("Time", marketData.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), marketData.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));

            GapBuy = (marketData.Ask - (marketData.Bid - 0.01m)).ToString("F5", CultureInfo.InvariantCulture);
            GapSell = ((marketData.Ask + 0.01m) - marketData.Bid).ToString("F5", CultureInfo.InvariantCulture);
        });
    }

    private void SetRowValue(string rowName, string sanAValue, string sanBValue)
    {
        var row = ParameterRows.FirstOrDefault(x => x.Name == rowName);
        if (row is null)
        {
            return;
        }

        row.ExchangeAValue = sanAValue;
        row.ExchangeBValue = sanBValue;
    }
}

public sealed class ParameterRowViewModel : ObservableObject
{
    private string _exchangeAValue;
    private string _exchangeBValue;

    public ParameterRowViewModel(string name, string exchangeAValue, string exchangeBValue)
    {
        Name = name;
        _exchangeAValue = exchangeAValue;
        _exchangeBValue = exchangeBValue;
    }

    public string Name { get; }

    public string ExchangeAValue
    {
        get => _exchangeAValue;
        set => SetProperty(ref _exchangeAValue, value);
    }

    public string ExchangeBValue
    {
        get => _exchangeBValue;
        set => SetProperty(ref _exchangeBValue, value);
    }
}
