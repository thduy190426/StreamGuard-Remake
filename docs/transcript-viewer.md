# Xem bản chép lời trong ứng dụng

Ngày 04/11/2026, nút mở thư mục trong tab Phụ đề trực tiếp được thay bằng **Xem bản chép lời**. Phần đọc mở ngay trong giao diện Driver Booster và dùng cùng cơ chế bảo vệ cửa sổ, con trỏ, tooltip và bản dịch hiện có.

- Chọn trong 200 phiên gần nhất, có nhãn ngày giờ và mã phiên để phân biệt.
- Chuyển TXT/SRT, chọn văn bản hoặc sao chép nội dung đang hiển thị.
- Theo dõi phiên hiện tại, tự cập nhật khoảng một giây khi đang xem tab phụ đề; chuyển sang lịch sử sẽ tắt theo dõi để không nhảy khỏi phiên đã chọn.
- Làm mới để nạp lại danh sách hoặc thử đọc lại file đã được mở khóa/khôi phục. Đóng phần xem không dừng nhận dạng hoặc đổi trạng thái bảo vệ.
- Khi tắt Theo dõi, bản đang xem không tự thay đổi theo timer hoặc pause/resume; Làm mới chủ động đọc thêm và giữ vùng chọn nếu văn bản chỉ được ghi nối. Nhãn trạng thái cho biết tự cập nhật đang tắt.

Đọc file chạy ngoài dispatcher, không ghi hoặc sửa bản chép lời. File đang ghi được mở với quyền chia sẻ phù hợp để không chặn CaptionTranscriptWriter. Trình đọc hiển thị tối đa 256 KiB cuối của file lớn và thông báo rõ; file đầy đủ vẫn giữ nguyên. Việc cắt dữ liệu không tạo ký tự thay thế do cắt ngang một ký tự UTF-8, và phần ký tự chưa ghi xong ở cuối file được chờ tới lượt cập nhật sau.

Các yêu cầu đọc cũ bị hủy/bỏ kết quả khi đổi phiên, định dạng, đóng phần xem hoặc cleanup; nút sao chép tạm khóa trong lúc nạp để tránh sao chép nhầm nội dung của phiên trước. Lỗi file thiếu/bị khóa chỉ hiện trong phần xem, không thay trạng thái nhận dạng. Các đường dẫn không phải mã phiên và file liên kết bị từ chối.

Bản cuối qua **3.010 điều kiện xác nhận C#**, build Release không có lỗi hoặc cảnh báo. Bộ kiểm tra gồm file đang được writer sở hữu, Unicode, SRT, cập nhật sau ghi thêm, file lớn, UTF-8 ghi dở, file bị khóa/xóa, khôi phục sau Làm mới, lịch sử 200 phiên, hủy đọc khi đóng, phiên trực tiếp và trạng thái bảo vệ cùng hoạt động. Các kiểm tra nhận dạng thật bằng WAV tổng hợp và hồi quy bảo vệ/khôi phục/giao diện hiện có cũng được chạy. Đã xem ảnh giao diện tiếng Việt và kiểm tra bố cục/nhãn phiên ở 860×600 và 640×460 cho cả hai ngôn ngữ.

Kiểm tra sử dụng cấu hình và transcript tạm; không chạy Loaded/OnStartup của ứng dụng thật hoặc thu âm thanh của người dùng. Backend Python và script trình duyệt không thay đổi trong đợt này. Ảnh thử cuối: `C:\Users\VirusBetaTester\AppData\Local\Temp\StreamGuard-localization-tests-5a2cf69e46a8460a9eed77efd9cdc7ca`.

Việc cập nhật bản chạy được xác nhận riêng trong `deployment-results.json` của đợt này, tại thư mục backup được ghi ở `%LOCALAPPDATA%\StreamGuardBuildTools\transcripts-backup-path.txt`; chỉ hoàn tất khi cả `PalStatus\release`, `StreamGuard\bin\Release\net8.0-windows` và các bản chạy liên quan đều verified.
