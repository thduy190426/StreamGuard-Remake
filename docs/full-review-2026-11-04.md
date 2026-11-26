# Rà soát toàn ứng dụng sau khi thêm trình đọc bản chép lời

Ngày 04/11/2026. Đợt rà soát này tiếp nối hai thay đổi gần nhất: tích hợp phụ đề và đọc bản chép lời ngay trong Driver Booster.

## Kết quả kiểm chứng

- **3.052 điều kiện xác nhận C#**, **25 kiểm tra Python**, **78 kiểm tra Chrome** đạt.
- Build Release với cảnh báo được coi là lỗi: không có lỗi/cảnh báo.
- Đã dựng giao diện tiếng Anh/Việt ở 860×600, 640×460, sidebar thu gọn/mở rộng, bản chép lời, cửa sổ phụ đề và Recovery có mục thất bại. Đã xem lại ảnh trình đọc và Recovery ở cửa sổ nhỏ.
- Kiểm tra C# tiếp tục chạy model CPU đóng gói thật với WAV tổng hợp, vòng đời tiến trình, sự cố tiến trình chủ và TXT/SRT. Không thu âm thanh của người dùng.

Đây là số assertion/test, không phải số tình huống người dùng độc lập hay bằng chứng không còn mọi lỗi.

## Lỗi và điểm UX đã sửa

| Vấn đề | Xử lý / bằng chứng |
|---|---|
| Hai lần bấm pause liên tiếp trước khi có phản hồi vẫn gửi hai lệnh pause | Theo dõi trạng thái yêu cầu riêng với trạng thái đã xác nhận. Fixture cố ý trì hoãn phản hồi, hai lần bấm nhanh phải trở lại Running và xuất đoạn sau resume. |
| Nút Bật có thể sáng khi phiên lỗi vẫn đang dọn tài nguyên, bấm lại bị bỏ qua | Dùng khả năng khởi động thực tế của session và thông báo khi Task đã hoàn tất. Ca UI chặn cleanup bằng barrier xác nhận nút tắt trong lúc dọn và tự bật lại sau khi worker được giải phóng. |
| Phản hồi ready/paused đến muộn có thể ghi đè trạng thái Stopping | Chặn cập nhật Running/Paused sau yêu cầu dừng; vẫn giữ cleanup và trạng thái kết thúc. |
| Cảnh báo không lưu được tùy chọn phụ đề bị lần cập nhật trạng thái kế tiếp xóa mất | Giữ lỗi lưu riêng với lỗi nhận dạng; cảnh báo chỉ hết sau lần lưu thành công. Kiểm tra file tùy chọn bị khóa, refresh UI rồi mở khóa/lưu lại. |
| Bỏ Theo dõi nhưng nội dung phiên hiện tại vẫn bị cập nhật, có thể mất vùng chọn | Tắt tự đọc khi bỏ Theo dõi; pause/resume chỉ cập nhật nhãn trạng thái. Làm mới chủ động giữ vùng chọn khi chỉ có văn bản ghi nối. Kiểm tra qua nhiều nhịp timer khi worker vẫn đang chạy. |
| Quét ứng dụng đang chạy có thể sửa danh sách/trạng thái sau cleanup | Không nhận yêu cầu mới hoặc áp dụng kết quả/lỗi đến muộn. Hai ca scan bị giữ bằng barrier mô phỏng thành công và lỗi sau cleanup, không thao tác ứng dụng thật. |
| File cấu hình bất thường có thể được đọc toàn bộ trước khi kiểm tra | Giới hạn 4 MiB trước khi nạp JSON, dùng backup nếu hợp lệ; không ghi cấu hình mới lớn hơn giới hạn rồi khiến chính ứng dụng không nạp lại được. Ca file 6 MiB đã thất bại trước sửa; ca ghi dữ liệu quá lớn xác nhận giữ nguyên file cấu hình tốt. |
| Nút pause/stop/copy trên overlay vẫn cho bấm dù chưa có phiên/nội dung phù hợp | Đồng bộ trạng thái enabled với Starting/Running/Paused/Stopping/Stopped và nội dung có thể sao chép. |
| Sidebar thu gọn thiếu tên truy cập; bộ chọn phiên không có viền focus rõ khi dùng bàn phím | Thêm tên truy cập theo ngôn ngữ cho cả sáu mục sidebar và viền focus cho bộ chọn phiên/định dạng; tiếp tục không hiện tooltip hoặc đổi con trỏ bàn tay. |

## Phạm vi hồi quy

Cấu hình/bản sao dự phòng/chuỗi null/tùy chọn thiết bị, phím tắt sai/trùng/tắt/tổ hợp nhiều phím bổ trợ, hủy tác vụ bảo vệ đang chờ, an toàn tiến trình hệ thống và tiến trình phụ đề, đối chiếu PID/đường dẫn/HWND, khôi phục và giữ thông tin khi thất bại, desktop do người dùng sở hữu, chính sách màn che, capture affinity đọc lại, các script clipboard trong Chrome, bản dịch, sidebar, icon và bindings đều nằm trong bộ hiện có đã chạy lại.

Phụ đề được thử cùng trạng thái bảo vệ trong lúc Start/Pause/Resume/Stop, xem lịch sử, đổi định dạng/ngôn ngữ, đọc file đang ghi, file bị khóa/xóa và đóng trình đọc khi I/O đang chờ. Không tự dừng phụ đề khi đổi trạng thái bảo vệ hoặc đọc lịch sử; không thay danh sách/phím tắt/quyền thiết bị từ các thao tác đó.

## Độ bao phủ đo được

Coverlet 6.0.4, chỉ tính assembly ứng dụng: **41,34% dòng**, **45,76% nhánh**, **58,03% phương thức**. Hash binary được đối chiếu trước/sau instrumentation; binary đã được hoàn nguyên trước triển khai.

| Thành phần | Dòng | Nhánh |
|---|---:|---:|
| CaptionService | 99,25% | 89,01% |
| CaptionTranscriptStore | 100,00% | 97,37% |
| MainWindow.Transcripts | 88,12% | 70,16% |
| MainWindow.Captions | 83,19% | 72,66% |
| CaptionOverlayWindow | 94,94% | 54,43% |
| ConfigService | 100,00% | 92,86% |
| RecoveryJournalStore | 100,00% | 95,24% |
| RecoveryService | 98,95% | 81,94% |
| ProcessSafetyService | 98,48% | 98,21% |
| MainWindow.Sidebar | 89,29% | 80,00% |

Không chạy OnStartup/Loaded của ứng dụng thật trong bộ kiểm tra vì có thể áp dụng quyền thiết bị, hosts/firewall, quản lý cửa sổ và phím tắt toàn cục. UI dùng Application cơ sở, cấu hình tạm, curtain/launcher/API giả khi cần; native chỉ thao tác HWND và tiến trình thử do bộ kiểm tra sở hữu. Chưa chứng minh hoạt động với mọi driver WASAPI, Bluetooth/HDMI/RDP, DPI/màn hình, mọi công cụ ghi hình, máy thiếu RAM hoặc vận hành liên tục nhiều giờ. Không có cơ sở cam kết tuyệt đối không còn bug hoặc xung đột trên mọi máy.

## Triển khai và bằng chứng

Cùng mã nguồn cuối dùng cho single-file Windows x64 ở `PalStatus\release`, bản đầy đủ ở `StreamGuard\bin\Release\net8.0-windows` và các bản `win-x64`, `win-x64\publish`. Kết quả thực tế nằm trong `deployment-results.json`; không coi hoàn tất nếu còn nhóm pending do file khóa.

Backup và bằng chứng: `C:\Users\VirusBetaTester\AppData\Local\CodexCleanupReports\DriverBooster-full-review-20261104-213802`. Bao gồm nguồn trước sửa, coverage JSON/Cobertura/CSV, hash cấu hình/dữ liệu gốc, hash nguồn cuối và danh sách đối chiếu bản cài. Ảnh và fixture của lượt kiểm tra cuối: `C:\Users\VirusBetaTester\AppData\Local\Temp\StreamGuard-localization-tests-f64b7e4cbf2c465380391be80a236dcd`.
