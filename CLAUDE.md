# Claude Rules for TradeDesktop

## Risk Assessment (Bắt buộc trước khi thực hiện)

Trước mỗi thay đổi, đánh giá rủi ro theo các tiêu chí sau:

- Thay đổi có ảnh hưởng đến logic giao dịch hiện tại không? (signal engine, state machine, gap calculation, trade execution)
- Thay đổi có ảnh hưởng đến luồng dữ liệu hiện tại không? (shared memory reader, config loading, event flow)
- Thay đổi có side effect ngoài phạm vi yêu cầu không?

**Nếu có bất kỳ rủi ro nào → dừng lại và thông báo cho user trước khi tiếp tục.**

## Có thể thay đổi logic nhưng cần đảm bào sự thay đổi không làm ảnh hưởng các chức năng khác. Cần có sự đảm bảo đúng cho phần sửa logic đó.

- Chỉ thêm hoặc sửa đúng những gì được yêu cầu rõ ràng.
- Không refactor, không dọn dẹp code xung quanh, không tối ưu hóa ngoài phạm vi task.
- Không thay đổi behavior của các method/service đang hoạt động, dù thấy có thể cải thiện.
- Khi thêm code mới, đảm bảo không làm thay đổi kết quả của code cũ.
