using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TradeDesktop.App.Commands;
using TradeDesktop.App.State;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;
using TradeDesktop.Application.Services;
using TradeDesktop.Domain.Models;

namespace TradeDesktop.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RuntimeConfigState _runtimeConfigState;
    private readonly IGapCalculator _gapCalculator;

    private string _runtimeSummary = string.Empty;
    private string _exchangeAHeader = "Sàn A";
    private string _exchangeBHeader = "Sàn B";
    private string _gapBuy = "0.00000";
    private string _gapSell = "0.00000";

    public DashboardViewModel(
        IServiceProvider serviceProvider,
        RuntimeConfigState runtimeConfigState,
        IConfigService configService,
        ISharedMemoryReader sharedMemoryReader,
        IGapCalculator gapCalculator)
    {
        _serviceProvider = serviceProvider;
        _runtimeConfigState = runtimeConfigState;
        _gapCalculator = gapCalculator;

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
        _ = InitializeRuntimeConfigAsync(configService);

        sharedMemoryReader.SnapshotReceived += OnSnapshotReceived;
        _ = sharedMemoryReader.StartAsync();
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
            $"IP: {_runtimeConfigState.CurrentIp}  |  Code: {_runtimeConfigState.CurrentCode}  |  Map 1: {_runtimeConfigState.MapName1}  |  Map 2: {_runtimeConfigState.MapName2}";
    }

    private async Task InitializeRuntimeConfigAsync(IConfigService configService)
    {
        var result = await configService.LoadByLocalIpAsync();
        if (result.IsSuccess && result.Exists)
        {
            _runtimeConfigState.Update(result.LocalIp, result.Code, result.MapName1, result.MapName2);
            return;
        }

        var warning = string.IsNullOrWhiteSpace(result.LocalIp)
            ? "[Config] Không lấy được IP local để tải config."
            : $"[Config] Không tìm thấy config cho IP hiện tại: {result.LocalIp}";

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            LogItems.Insert(0, warning);
            if (!string.IsNullOrWhiteSpace(result.LocalIp))
            {
                _runtimeConfigState.Update(result.LocalIp, _runtimeConfigState.CurrentCode, _runtimeConfigState.MapName1, _runtimeConfigState.MapName2);
            }
        });
    }

    private void OnSnapshotReceived(object? sender, SharedMemorySnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _runtimeConfigState.UpdateDashboardMetrics(snapshot);

            BindExchangeToRows(snapshot.SanA, snapshot.SanB, snapshot.TimestampUtc);

            var (gapBuy, gapSell) = _gapCalculator.Calculate(snapshot.SanA, snapshot.SanB);
            GapBuy = FormatNumberOrDash(gapBuy);
            GapSell = FormatNumberOrDash(gapSell);
        });
    }

    private void BindExchangeToRows(ExchangeMetrics sanA, ExchangeMetrics sanB, DateTime timestampUtc)
    {
        SetRowValue("Symbol", FormatTextOrDash(sanA.Symbol), FormatTextOrDash(sanB.Symbol));
        SetRowValue("Bid", FormatNumberOrDash(sanA.Bid), FormatNumberOrDash(sanB.Bid));
        SetRowValue("Ask", FormatNumberOrDash(sanA.Ask), FormatNumberOrDash(sanB.Ask));

        var spreadA = sanA.Spread ?? CalculateSpread(sanA.Bid, sanA.Ask);
        var spreadB = sanB.Spread ?? CalculateSpread(sanB.Bid, sanB.Ask);
        SetRowValue("Spread", FormatNumberOrDash(spreadA), FormatNumberOrDash(spreadB));

        SetRowValue("Latency(ms)", FormatNumberOrDash(sanA.LatencyMs, 0), FormatNumberOrDash(sanB.LatencyMs, 0));
        SetRowValue("TPS", FormatNumberOrDash(sanA.Tps, 0), FormatNumberOrDash(sanB.Tps, 0));
        SetRowValue("Time", FormatTextOrDash(sanA.Time ?? timestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)), FormatTextOrDash(sanB.Time ?? timestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
        SetRowValue("Max Lat(ms)", FormatNumberOrDash(sanA.MaxLatMs, 0), FormatNumberOrDash(sanB.MaxLatMs, 0));
        SetRowValue("Avg Lat(ms)", FormatNumberOrDash(sanA.AvgLatMs, 0), FormatNumberOrDash(sanB.AvgLatMs, 0));
    }

    private static decimal? CalculateSpread(decimal? bid, decimal? ask)
    {
        if (!bid.HasValue || !ask.HasValue)
        {
            return null;
        }

        return ask.Value - bid.Value;
    }

    private static string FormatNumberOrDash(decimal? value, int decimalPlaces = 5)
        => value.HasValue
            ? value.Value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)
            : "-";

    private static string FormatTextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

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
