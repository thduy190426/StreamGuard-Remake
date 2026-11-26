# Cải thiện giao diện Driver Booster

Hoàn tất phần giao diện ngày 04/11/2026. Phạm vi thay đổi là trình bày WPF; không thay đổi bộ nhận dạng phụ đề, phím tắt hay các dịch vụ bảo vệ/khôi phục.

- Dùng chung icon vector cho các nút bật/dừng/tạm dừng phụ đề, sao chép, thư mục, hiện/ẩn, thử khôi phục và thao tác ứng dụng. Giữ nguyên `Content`, `Tag`, sự kiện và dữ liệu của nút để tương thích với mã điều khiển hiện tại.
- Bổ sung tiêu đề có icon và mô tả cho các màn hình quản lý ứng dụng, bảo mật, hướng dẫn, phụ đề và khôi phục. Dùng cỡ chữ, thẻ nền và khoảng cách thống nhất.
- Tăng độ tương phản của chữ phụ, làm sáng nhẹ nền thẻ và đường viền; giữ hệ màu tối, tím nhấn, xanh thành công và đỏ cảnh báo.
- Thêm nhãn ngoại tuyến, giọng nói tiếng Anh và TXT/SRT để làm rõ phạm vi của phụ đề.
- Tô màu trạng thái khôi phục theo dữ liệu hiện có: đã khôi phục, thất bại, cần xác minh và đã đóng. Bổ sung icon thông báo tương ứng.
- Thay các ký tự icon phụ thuộc font trên thanh phụ đề bằng vector; icon tạm dừng/tiếp tục phản ánh thao tác hiện tại. Nút chỉ có icon có tên hỗ trợ truy cập đổi theo ngôn ngữ. Theo yêu cầu tiếp theo, tooltip khi rê chuột đã được tắt trên toàn ứng dụng, kể cả cửa sổ phụ đề.
- Nhãn checkbox dài có thể xuống dòng trong chiều rộng hiện tại. Hàng ứng dụng tách tên khỏi các nhãn tiến trình/trạng thái, bỏ trạng thái lặp lại và cho các nhãn tự xuống hàng.
- Các nhãn mới hỗ trợ tiếng Anh và tiếng Việt; giữ hành vi thu gọn/kéo sidebar và bố cục nội dung chính.

Build Release qua **2.786 kiểm tra/assertion** C# hiện có, không có lỗi hoặc cảnh báo. Đã dựng và xem giao diện ở 860×600, 640×460, sidebar rộng 200/300 px, màn hình khôi phục có mục thất bại và cửa sổ phụ đề nổi ở cả hai ngôn ngữ. Bộ kiểm tra tiếp tục kiểm tra vòng đời nhận dạng, khôi phục, cấu hình và WPF bindings; số kiểm tra không phải tỷ lệ bao phủ mọi hành vi.

Bộ dựng theme dùng `Application` cơ sở và tài nguyên riêng để không kích hoạt `App.OnStartup`. Một lần thử nạp theme qua lớp `App` đã bộc lộ việc dispatcher có thể gọi startup thật; cách thử đó đã được loại bỏ. Hash cấu hình người dùng được xác nhận giữ nguyên. Các lượt kiểm tra cuối không chạy sự kiện Loaded của cửa sổ chính và không áp dụng quyền thiết bị/firewall/hosts.

Bản chạy cần được đối chiếu SHA-256 tại cả `PalStatus\release` và `StreamGuard\bin\Release\net8.0-windows`, đồng thời giữ nhất quán các bản `win-x64` và `win-x64\publish`. Nếu ứng dụng khóa DLL, bản mới được giữ ở staging để hoàn tất sau khi ứng dụng thoát. Không chỉ thay EXE của bản framework-dependent. Cache build phát sinh trong cây nguồn được chuyển vào Thùng rác sau khi hoàn tất kiểm tra.
