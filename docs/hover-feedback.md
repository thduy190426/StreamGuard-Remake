# Con trỏ và gợi ý khi rê chuột

Driver Booster giữ con trỏ mũi tên trong giao diện WPF, bao gồm nút, sidebar, bộ chọn ngôn ngữ, menu và cửa sổ phụ đề. Kéo sidebar và nhập văn bản vẫn hoạt động, dù con trỏ không đổi thành hình kéo ngang hoặc dấu nhập chữ.

Tooltip khi rê chuột bị chặn qua sự kiện chung của tiến trình, kể cả các phần tử tạo sau lúc khởi động và cây giao diện riêng của menu/dropdown. Chuỗi mô tả và tên hỗ trợ truy cập vẫn được giữ để không phá dữ liệu dịch hoặc hỗ trợ truy cập. Đây là thay đổi trong ứng dụng; không sửa bộ con trỏ Windows hoặc ứng dụng khác.

Biểu tượng khay hệ thống giữ menu và thao tác nhấp đúp, nhưng không có chữ gợi ý khi rê chuột, tooltip của menu hay thông báo balloon. Nút khôi phục khẩn cấp vẫn cập nhật trạng thái trong cửa sổ ứng dụng. Thông báo lỗi cần phản hồi trực tiếp vẫn được giữ.

Bộ kiểm tra xác nhận chặn sự kiện tooltip/con trỏ trên cửa sổ chính, cửa sổ phụ đề, nút, checkbox, ô nhập, thanh kéo, bộ chọn và phần tử menu tách riêng. Có trường hợp sự kiện đã được đánh dấu xử lý và tooltip được gán lại sau khởi tạo. Kiểm tra hồi quy hiện có tiếp tục kiểm tra đổi ngôn ngữ, kéo/thu gọn sidebar, quản lý cửa sổ, khôi phục và phụ đề; không bật các chức năng bảo vệ thật hoặc ghi âm máy người dùng trong quá trình kiểm tra.
