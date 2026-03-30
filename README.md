# TradeDesktop - WPF Trading Dashboard (.NET 8)

README này tập trung vào **công thức** và **business logic giao dịch** của dự án, đồng thời liệt kê nhanh các file quan trọng để dễ onboarding.

---

## 1) Tổng quan ngắn

`TradeDesktop` là ứng dụng WPF (.NET 8) đọc dữ liệu từ shared memory (hoặc mock), tính toán chênh lệch giữa 2 sàn (A/B), xác nhận tín hiệu theo thời gian, và phát sinh lệnh `OPEN/CLOSE` theo flow.

Kiến trúc theo layer:

```text
TradeDesktop.sln
├─ TradeDesktop.App/             # UI (WPF + ViewModel)
├─ TradeDesktop.Application/     # Business logic + use-case + abstraction
├─ TradeDesktop.Domain/          # Domain models thuần
├─ TradeDesktop.Infrastructure/  # Kết nối data source, repository, signal infra
└─ TradeDesktop.Tests/           # Unit tests cho logic chính
```

---

## 2) Công thức cốt lõi (quan trọng nhất)

### 2.1 Công thức Gap

Được tính trong `TradeDesktop.Application/Services/GapCalculator.cs`:

- `GapBuy = (B.Bid - A.Ask) * Point`
- `GapSell = (B.Ask - A.Bid) * Point`

Trong code, kết quả được ép về `int`:

- `gapBuy = (int)((sanB.Bid - sanA.Ask) * pointMultiplier)`
- `gapSell = (int)((sanB.Ask - sanA.Bid) * pointMultiplier)`

> `Point` lấy từ runtime config; nếu cấu hình không hợp lệ (`<=0`) thì fallback về `1`.

### 2.2 Ý nghĩa nghiệp vụ

- `GapBuy` lớn dương: thiên hướng mở theo nhánh **GapBuy**.
- `GapSell` lớn âm: thiên hướng mở theo nhánh **GapSell** (vì rule dùng ngưỡng âm).
- Hệ thống luôn xử lý theo **cửa sổ xác nhận theo thời gian**, không bắn lệnh chỉ với 1 tick đơn lẻ.

---

## 3) Business logic giao dịch

## 3.1 Open Signal (xác nhận mở lệnh)

Code chính: `TradeDesktop.Application/Services/GapSignalConfirmationEngine.cs`

Hệ thống chuẩn hoá config trước khi dùng:

- `ConfirmGapPts = Abs(config.ConfirmGapPts)`
- `OpenPts = Abs(config.OpenPts)`
- `HoldConfirmMs = Max(0, config.HoldConfirmMs)`

### A) OpenByGapBuy

Điều kiện:

1. Tick hiện tại có `GapBuy` và `GapBuy >= ConfirmGapPts`.
2. Bắt đầu gom dữ liệu trong cửa sổ thời gian.
3. Sau khi đủ `HoldConfirmMs`, **toàn bộ** gap trong cửa sổ phải thoả `>= ConfirmGapPts`.
4. Tick cuối cùng trong cửa sổ phải thoả `>= OpenPts`.
5. Khi thoả hết điều kiện -> trigger `OpenByGapBuy`.

### B) OpenByGapSell

Điều kiện đối xứng theo ngưỡng âm:

1. Tick hiện tại có `GapSell` và `GapSell <= -ConfirmGapPts`.
2. Giữ điều kiện liên tục đủ `HoldConfirmMs`.
3. Toàn bộ cửa sổ phải thoả `<= -ConfirmGapPts`.
4. Tick cuối cùng phải thoả `<= -OpenPts`.
5. Khi thoả -> trigger `OpenByGapSell`.

> Nếu bất kỳ điều kiện nào fail trong cửa sổ, state của nhánh đó bị reset.

## 3.2 Close Signal (xác nhận đóng lệnh)

Code chính: `TradeDesktop.Application/Services/CloseSignalEngine.cs`

Chuẩn hoá config close:

- `CloseConfirmGapPts = Abs(config.CloseConfirmGapPts)`
- `ClosePts = Abs(config.ClosePts)`
- `CloseHoldConfirmMs = Max(0, config.CloseHoldConfirmMs)`

Rule theo mode đã mở:

- Nếu đang mở theo `GapBuy` (`TradingOpenMode.GapBuy`)  
  -> close theo nhánh `GapSell` với điều kiện âm (`<= -CloseConfirmGapPts`, tick cuối `<= -ClosePts`).

- Nếu đang mở theo `GapSell` (`TradingOpenMode.GapSell`)  
  -> close theo nhánh `GapBuy` với điều kiện dương (`>= CloseConfirmGapPts`, tick cuối `>= ClosePts`).

## 3.3 Flow trạng thái giao dịch (state machine)

Code chính: `TradeDesktop.Application/Services/TradingFlowEngine.cs`

Các trạng thái:

- `WaitingOpen`
- `WaitingCloseFromGapBuy`
- `WaitingCloseFromGapSell`

Luồng:

1. `WaitingOpen`: kiểm tra open signal.
2. Khi open thành công:
   - Ghi `OpenedAtUtc`
   - Random `CurrentHoldingSeconds` trong `[StartTimeHold..EndTimeHold]`
   - Chuyển sang `WaitingCloseFromGapBuy` hoặc `WaitingCloseFromGapSell`
3. Ở trạng thái close: chỉ bắt đầu kiểm tra close sau khi đã giữ lệnh đủ `CurrentHoldingSeconds`.
4. Khi close thành công:
   - Ghi `ClosedAtUtc`
   - Random `CurrentWaitSeconds` trong `[StartWaitTime..EndWaitTime]`
   - Trả về `WaitingOpen`
5. Trong `WaitingOpen`, nếu chưa qua đủ `CurrentWaitSeconds` kể từ `ClosedAtUtc` thì chưa được mở lệnh mới.

> Ý nghĩa nghiệp vụ: chống vào/ra lệnh quá dày, mô phỏng nhịp giao dịch thực tế.

---

## 4) Mapping công thức sang log tín hiệu

Hai file chính:

- `TradeDesktop.Application/Services/TradeInstructionFactory.cs`
- `TradeDesktop.Application/Services/TradeSignalLogBuilder.cs`

Khi trigger xảy ra:

1. Tạo `TradeSignalInstruction` gồm:
   - loại trigger (`OpenByGapBuy`, `CloseByGapSell`, ...)
   - danh sách gap dùng để confirm
   - biểu thức giá nguồn để giải thích tín hiệu
2. Build log theo format:
   - Header: `[OPEN BY GAP_BUY] GAP ...`
   - Explain line: `= (B.Bid - A.Ask) * Point(x)` hoặc `= (B.Ask - A.Bid) * Point(x)`
   - 2 dòng leg cho sàn A và B (OPEN/CLOSE, BUY/SELL, giá)

=> Giúp trace rõ “vì sao hệ thống bắn tín hiệu”.

---

## 5) Danh sách file quan trọng và chức năng

## 5.1 App (UI)

- `TradeDesktop.App/ViewModels/DashboardViewModel.cs`  
  Trung tâm UI/business orchestration: nhận snapshot, bind số liệu, gọi `TradingFlowEngine`, dựng log tín hiệu, hiển thị trạng thái giao dịch.

- `TradeDesktop.App/MainWindow.xaml`  
  Dashboard hiển thị dữ liệu 2 sàn, gap, trạng thái trading logic, signal log.

- `TradeDesktop.App/Commands/AsyncRelayCommand.cs`  
  Command async cho WPF, có chặn chạy song song.

## 5.2 Application (business layer)

- `TradeDesktop.Application/Services/GapCalculator.cs`  
  Tính `GapBuy/GapSell` theo công thức cốt lõi.

- `TradeDesktop.Application/Services/GapSignalConfirmationEngine.cs`  
  Engine xác nhận mở lệnh theo cửa sổ thời gian và ngưỡng confirm/open.

- `TradeDesktop.Application/Services/CloseSignalEngine.cs`  
  Engine xác nhận đóng lệnh theo mode đang mở và ngưỡng close.

- `TradeDesktop.Application/Services/TradingFlowEngine.cs`  
  State machine điều phối OPEN/CLOSE + random hold/wait.

- `TradeDesktop.Application/Services/TradeInstructionFactory.cs`  
  Chuyển `GapSignalTriggerResult` thành instruction chi tiết 2 leg A/B.

- `TradeDesktop.Application/Services/TradeSignalLogBuilder.cs`  
  Render instruction thành log text phục vụ vận hành/debug.

- `TradeDesktop.Application/Models/GapSignalModels.cs`  
  Định nghĩa enum/record cho snapshot, config, trigger, instruction.

- `TradeDesktop.Application/Services/DashboardMetricsMapper.cs`  
  Map raw shared memory snapshot -> `DashboardMetrics` và tính gap.

- `TradeDesktop.Application/Services/ConfigService.cs`  
  Load/save runtime config theo machine host name; chuẩn hoá giá trị config.

## 5.3 Domain

- `TradeDesktop.Domain/Models/DashboardMetrics.cs`  
  Model aggregate dữ liệu hiển thị dashboard.

- `TradeDesktop.Domain/Models/MarketData.cs`, `SignalResult.cs`, `SignalType.cs`  
  Model tín hiệu cơ bản (phần nền cũ/simple signal).

## 5.4 Infrastructure

- `TradeDesktop.Infrastructure/MarketData/SharedMemoryMarketDataReader.cs`  
  Reader dữ liệu shared memory thực tế.

- `TradeDesktop.Infrastructure/MarketData/MockSharedMemoryMarketDataReader.cs`  
  Reader mock phục vụ test/dev.

- `TradeDesktop.Infrastructure/Supabase/SupabaseConfigRepository.cs`  
  Truy xuất config từ Supabase theo host name.

## 5.5 Tests (nên đọc đầu tiên khi cần hiểu rule)

- `TradeDesktop.Tests/GapCalculatorTests.cs`
- `TradeDesktop.Tests/GapSignalConfirmationEngineTests.cs`
- `TradeDesktop.Tests/CloseSignalEngineTests.cs`
- `TradeDesktop.Tests/TradingFlowEngineTests.cs`
- `TradeDesktop.Tests/TradeSignalLogBuilderTests.cs`

---

## 6) Những thứ quan trọng cần nhớ

1. **Point multiplier** tác động trực tiếp độ lớn gap -> sai point là sai toàn bộ tín hiệu.
2. **Confirm theo cửa sổ thời gian** là lõi anti-noise; không nên bỏ nếu chưa có bộ lọc khác thay thế.
3. **Open và Close dùng ngưỡng độc lập** (`OpenPts/ConfirmGapPts` khác `ClosePts/CloseConfirmGapPts`).
4. **TradingFlowEngine có hold + wait random** để tránh spam giao dịch liên tục.
5. **Config theo machine host name**: sai hostname sẽ không load đúng config runtime.
6. Khi debug tín hiệu, luôn đối chiếu theo thứ tự:  
   `Snapshot giá -> Gap -> Confirm window -> Trigger -> Instruction -> Log line`.

---

## 7) Build / Run / Test

```bash
dotnet restore TradeDesktop.sln
dotnet build TradeDesktop.sln
dotnet test TradeDesktop.Tests/TradeDesktop.Tests.csproj
```

Chạy app:

```bash
dotnet run --project TradeDesktop.App/TradeDesktop.App.csproj
```