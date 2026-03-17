# TradeDesktop - WPF Trading Dashboard (.NET 8)

README này giải thích chi tiết codebase hiện tại để bạn có thể nắm nhanh kiến trúc, luồng dữ liệu, và cách mở rộng dự án.

---

## 1) Tổng quan dự án

`TradeDesktop` là ứng dụng desktop Windows dùng **WPF + MVVM** trên **.NET 8**.  
Hiện tại app đã có màn hình dashboard cơ bản để hiển thị market data realtime (mock), tính tín hiệu giao dịch đơn giản và hiển thị lý do của tín hiệu.

### Các chức năng đã có

- Nút **Start/Stop** stream dữ liệu.
- Hiển thị: `Connection`, `Bid`, `Ask`, `Spread`, `Timestamp`, `Signal`, `Reason`.
- Dữ liệu thị trường mock phát mỗi 1 giây.
- Tính signal dạng `Buy / Sell / Hold`.
- Có unit test cơ bản cho signal engine.

---

## 2) Cấu trúc solution

```text
TradeDesktop.sln
├─ TradeDesktop.App/             # Presentation (WPF + MVVM)
├─ TradeDesktop.Application/     # Use-case + abstraction/interfaces
├─ TradeDesktop.Domain/          # Domain models thuần
├─ TradeDesktop.Infrastructure/  # Implement cụ thể (mock data, signal engine)
└─ TradeDesktop.Tests/           # Unit tests (xUnit)
```

### Ý nghĩa từng layer

1. **Domain**
   - Chứa model cốt lõi, không phụ thuộc framework/UI.
   - Ví dụ:
     - `MarketData` (Bid/Ask/Timestamp/IsConnected + Spread)
     - `SignalType` (`Hold`, `Buy`, `Sell`)
     - `SignalResult` (signal + reason)

2. **Application**
   - Chứa interface và use-case/service trung gian.
   - Không chứa chi tiết hạ tầng cụ thể.
   - Ví dụ:
     - `IMarketDataReader`, `ISharedMemoryReader`, `ISignalEngine`
     - `IDashboardService`/`DashboardService`

3. **Infrastructure**
   - Chứa implementation thực tế cho interface ở Application.
   - Hiện tại gồm:
     - `MockSharedMemoryMarketDataReader`: giả lập luồng giá mỗi 1 giây.
     - `SimpleSignalEngine`: luật signal đơn giản theo spread và ngưỡng giá.

4. **App (WPF)**
   - UI + ViewModel + command.
   - Dùng DI qua `Host.CreateDefaultBuilder()` để resolve dependency.

---

## 3) Giải thích source code theo file chính

## `TradeDesktop.App`

- **`App.xaml.cs`**
  - Khởi tạo DI container bằng `Host.CreateDefaultBuilder()`.
  - Gọi `AddApplication()` và `AddInfrastructure()` để đăng ký service.
  - Register `DashboardViewModel` và `MainWindow` dạng singleton.
  - On startup: start host rồi show `MainWindow`.
  - On exit: stop và dispose host.

- **`MainWindow.xaml`**
  - View dashboard hiển thị dữ liệu thị trường và tín hiệu.
  - Binding đến các property của `DashboardViewModel`.
  - Nút `Start`/`Stop` binding tới `StartCommand`/`StopCommand`.

- **`MainWindow.xaml.cs`**
  - Constructor nhận `DashboardViewModel` qua DI.
  - Set `DataContext = viewModel`.

- **`ViewModels/ObservableObject.cs`**
  - Base class triển khai `INotifyPropertyChanged`.
  - Có `SetProperty` để update property + bắn event UI.

- **`ViewModels/DashboardViewModel.cs`**
  - Là trung tâm của UI logic.
  - Inject:
    - `IMarketDataReader`: nguồn dữ liệu thị trường.
    - `IDashboardService`: dịch vụ tính signal.
  - Đăng ký event `_marketDataReader.MarketDataReceived += OnMarketDataReceived`.
  - `StartAsync()` / `StopAsync()` điều khiển stream.
  - `OnMarketDataReceived(...)`:
    - Marshal về UI thread qua `Application.Current.Dispatcher.Invoke(...)`.
    - Cập nhật Bid/Ask/Spread/Timestamp/Connection.
    - Gọi service tính signal và cập nhật `Signal`, `Reason`.

- **`Commands/AsyncRelayCommand.cs`**
  - Command bất đồng bộ cho WPF.
  - Chặn chạy song song bằng cờ `_isRunning`.
  - Hỗ trợ điều kiện `CanExecute` + `RaiseCanExecuteChanged`.

## `TradeDesktop.Application`

- **`Abstractions/IMarketDataReader.cs`**
  - Contract cho reader dữ liệu thị trường:
    - Event `MarketDataReceived`
    - `IsRunning`
    - `StartAsync()` / `StopAsync()`

- **`Abstractions/ISharedMemoryReader.cs`**
  - Kế thừa `IMarketDataReader`.
  - Dùng để tách rõ loại nguồn dữ liệu (shared memory).

- **`Abstractions/ISignalEngine.cs`**
  - Contract tính signal từ `MarketData`.

- **`Services/DashboardService.cs`**
  - `IDashboardService` có 1 hàm `EvaluateSignal(...)`.
  - Implementation chỉ delegate cho `ISignalEngine.Calculate(...)`.
  - Vai trò: tạo điểm mở rộng use-case dashboard.

- **`DependencyInjection.cs`**
  - Extension method `AddApplication(...)` để đăng ký service Application.

## `TradeDesktop.Infrastructure`

- **`MarketData/MockSharedMemoryMarketDataReader.cs`**
  - Reader giả lập dữ liệu:
    - Tạo tick mỗi giây bằng `PeriodicTimer`.
    - Sinh giá quanh `_lastMidPrice` với random drift/spread.
    - Tạo `MarketData` với `Timestamp = DateTime.UtcNow` và `IsConnected = true`.
  - Có lock `_syncRoot` để đảm bảo start/stop thread-safe.

- **`Signals/SimpleSignalEngine.cs`**
  - Luật signal hiện tại:
    1. Không connected -> `Hold` (`No market connection`)
    2. `Spread > 0.03` -> `Hold` (`Spread too wide`)
    3. `Bid >= 100.10` -> `Sell`
    4. `Ask <= 99.90` -> `Buy`
    5. Còn lại -> `Hold` (`No edge in current range`)

- **`DependencyInjection.cs`**
  - Mapping interface -> implementation:
    - `ISharedMemoryReader` -> `MockSharedMemoryMarketDataReader`
    - `IMarketDataReader` -> cùng instance của `ISharedMemoryReader`
    - `ISignalEngine` -> `SimpleSignalEngine`

## `TradeDesktop.Domain`

- **`MarketData.cs`**: record immutable cho dữ liệu giá, có computed property `Spread = Ask - Bid`.
- **`SignalType.cs`**: enum `Hold/Buy/Sell`.
- **`SignalResult.cs`**: record chứa kết quả signal + lý do.

## `TradeDesktop.Tests`

- **`SimpleSignalEngineTests.cs`** (xUnit)
  - Test case đang có:
    - Disconnected -> `Hold`
    - Bid cao hơn ngưỡng -> `Sell`

---

## 4) Luồng chạy end-to-end

1. App start -> DI container build.
2. `MainWindow` mở với `DashboardViewModel` làm DataContext.
3. User bấm **Start** -> `IMarketDataReader.StartAsync()`.
4. Mock reader phát tick mỗi giây qua event `MarketDataReceived`.
5. ViewModel nhận event, cập nhật UI fields.
6. ViewModel gọi `IDashboardService.EvaluateSignal(marketData)`.
7. Signal trả về và hiển thị `Signal` + `Reason`.
8. User bấm **Stop** -> reader dừng, trạng thái chuyển `Disconnected`.

---

## 5) Build / Run / Test

> Lưu ý: WPF app chạy trên Windows. Unit test có thể chạy độc lập.

```bash
dotnet restore TradeDesktop.sln
dotnet build TradeDesktop.sln
dotnet test TradeDesktop.Tests/TradeDesktop.Tests.csproj
```

Chạy app:

```bash
dotnet run --project TradeDesktop.App/TradeDesktop.App.csproj
```

---

## 5.1) Chạy trên Windows **không cần cài gì thêm**

Nếu bạn chỉ muốn tải về và chạy ngay trên máy Windows (không cài .NET, không cài Visual Studio), thì nên dùng **file build sẵn (.exe)** từ GitHub Release.

### Cách làm nhanh nhất

1. Vào trang **Releases** của repo.
2. Tải file:
   - `TradeDesktop.App-latest.exe` (luôn là bản mới nhất), hoặc
   - `TradeDesktop.App-<version>.exe` (bản theo version cụ thể).
3. Double-click file `.exe` để chạy.

> Vì app publish dạng self-contained/single-file nên người dùng cuối không cần cài .NET runtime.

### Lưu ý quan trọng

- Nếu bạn tải **source code từ git** (zip/clone) thì đó chỉ là mã nguồn, **không chạy trực tiếp được** nếu không build.
- Muốn “không cài gì trên máy Windows”, bạn cần tải đúng **file `.exe` đã được build sẵn** từ Release.
- Nếu Windows SmartScreen cảnh báo, chọn **More info -> Run anyway** (khi bạn tin tưởng nguồn file).

## 5.2) Trường hợp cần chạy từ source code (có cài môi trường)

Nếu bạn muốn tự build từ source thì mới cần cài .NET SDK/Visual Studio.

---

## 5.3) Troubleshoot lỗi "double-click exe nhưng không thấy app"

Nếu app thoát im lặng trên Windows sau khi build/publish, hãy kiểm tra theo thứ tự sau:

1. **Mở log startup**
   - App hiện đã ghi log tại:
   - `%LOCALAPPDATA%\TradeDesktop\logs\startup.log`
   - Nếu startup lỗi, app sẽ hiển thị popup kèm đường dẫn file log.

2. **Publish đúng runtime Windows**

```bash
dotnet publish TradeDesktop.App/TradeDesktop.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

3. **Nếu vẫn lỗi, thử bản không single-file để khoanh vùng**

```bash
dotnet publish TradeDesktop.App/TradeDesktop.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false
```

4. **Kiểm tra Event Viewer trên Windows**
   - Mở: `Event Viewer -> Windows Logs -> Application`
   - Tìm lỗi nguồn `.NET Runtime` hoặc `Application Error` tại thời điểm chạy app.

5. **Các nguyên nhân hay gặp**
   - Build/publish sai kiến trúc (x64/x86/arm64).
   - Dependency khởi tạo sớm bị exception (shared memory/config/network) làm app thoát trước khi render UI.
   - Thiếu file/phần native khi publish.

---

## 6) CI/CD và `.env`

- Bạn đã có `.github/workflows/build.yml` để build/release.
- File `.env` đã được tạo để gom các key cấu hình build như:
  - `APP_NAME`, `MAJOR`, `MINOR`, `PATCH`, `VERSION`, `VERSIONED_EXE`, `LATEST_EXE`, `TAG`, `RUNTIME`, `DOTNET_VERSION`.

> Ghi chú: workflow đã được chỉnh publish đúng đường dẫn `TradeDesktop.App/TradeDesktop.App.csproj` và đã thêm `permissions: contents: write` để tạo release thành công.

---

## 7) Hướng mở rộng tiếp theo (gợi ý)

1. Thay mock reader bằng reader thật từ shared memory.
2. Bổ sung logging + error handling chi tiết hơn ở reader/viewmodel.
3. Mở rộng signal engine (EMA, RSI, breakout, risk filters...).
4. Thêm nhiều unit test theo từng rule signal.
5. Tách config ngưỡng signal ra file cấu hình để dễ chỉnh runtime.