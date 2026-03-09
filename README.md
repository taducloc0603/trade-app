# TradeDesktop - .NET 8 WPF Market Dashboard

Initial clean-architecture solution scaffold for a Windows desktop trading UI using **C# .NET 8** and **WPF (MVVM)**.

## Solution Structure

- `TradeDesktop.App` (WPF)
  - Presentation layer (MVVM)
  - `DashboardViewModel`, `MainWindow`, commands, app startup/DI wiring
- `TradeDesktop.Domain` (Class Library)
  - Core models: `MarketData`, `SignalResult`, `SignalType`
- `TradeDesktop.Application` (Class Library)
  - Use-case/contracts layer
  - Interfaces: `ISharedMemoryReader`, `IMarketDataReader`, `ISignalEngine`
  - Service: `IDashboardService` / `DashboardService`
- `TradeDesktop.Infrastructure` (Class Library)
  - Implementations for external concerns
  - `MockSharedMemoryMarketDataReader` publishes mock Bid/Ask every second
  - `SimpleSignalEngine` returns Buy/Sell/Hold + reason
- `TradeDesktop.Tests` (xUnit)
  - Initial unit tests for signal engine behavior

## Implemented Features

- Clean modular layering (Domain/Application/Infrastructure/App)
- MVVM-based dashboard screen
- Dependency Injection setup via `Microsoft.Extensions.Hosting`
- Mock shared memory reader placeholder (realtime-like 1-second ticks)
- Signal calculation pipeline wired to UI
- Main screen displays:
  - connection status
  - bid / ask / spread
  - timestamp
  - signal and reason
  - start/stop controls

## High-Level Flow

1. User clicks **Start**.
2. `DashboardViewModel` starts `ISharedMemoryReader`.
3. Mock reader emits market ticks every second.
4. ViewModel forwards data to `IDashboardService`.
5. Service calls `ISignalEngine` and returns signal result.
6. UI updates all dashboard fields in realtime.

## Run (on Windows with .NET 8 SDK)

```bash
dotnet build TradeDesktop.sln
dotnet run --project TradeDesktop.App/TradeDesktop.App.csproj
```

## Notes

- Current market data source is a mock placeholder and can be replaced by a real shared memory implementation by introducing a new `ISharedMemoryReader` in `Infrastructure`.
- Signal logic is intentionally simple and isolated for easy extension.