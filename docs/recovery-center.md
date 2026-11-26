# Trung tâm khôi phục / Recovery Center

Tính năng bổ sung ngày 02/11/2026 cho Driver Booster. Màn hình nằm tại thanh bên, hỗ trợ đổi trực tiếp giữa tiếng Việt và tiếng Anh.

## Cách sử dụng

Mỗi mục tương ứng với một cửa sổ, bao gồm tên, tiêu đề đã ghi nhận, đường dẫn ứng dụng, cách bảo vệ và phiên tạo bản ghi. Trạng thái gồm đang quản lý, đang chờ, thất bại, đã khôi phục, cửa sổ đã đóng và cần xác minh.

- **Thử khôi phục lại**: thử trả một cửa sổ còn chờ về desktop, kiểu hiển thị và vị trí gốc. Mục thất bại được giữ lại cùng thông báo lỗi để thử sau.
- **Khôi phục tất cả**: xử lý các mục chưa hoàn tất, kể cả phiên trước. Mỗi mục vẫn được xác minh riêng.
- **Kiểm tra lại**: xác minh cửa sổ còn tồn tại và đúng định danh. Thao tác này không di chuyển hay hiện cửa sổ.

Khi yêu cầu khôi phục, màn che được gỡ và chế độ che tự động tạm dừng để người dùng nhìn thấy kết quả. Có thể tiếp tục che tự động bằng điều khiển sẵn có sau đó. Trạng thái bảo vệ của từng ứng dụng được đồng bộ khi cửa sổ đã được trả về.

Khi mở lại ứng dụng, mục đang quản lý từ phiên trước chuyển thành đang chờ. Nếu có mục chưa hoàn tất của phiên trước, Trung tâm khôi phục được mở sẵn; việc mở ứng dụng không tự thực hiện phục hồi các cửa sổ đó. Phím khôi phục khẩn cấp **Ctrl+Shift+Alt+H** cũng thử các mục đang chờ của phiên trước. Dừng bảo vệ hoặc thoát bình thường tiếp tục xử lý cửa sổ của phiên hiện tại.

## Thông tin được lưu

Nhật ký nằm tại `%APPDATA%\StreamGuard\config.recovery.json`, cạnh cấu hình hiện có nhưng là một file riêng. Nhật ký ghi tiêu đề và đường dẫn ứng dụng cùng thông tin kỹ thuật cần để trả cửa sổ về; không chứa nội dung tài liệu hoặc clipboard. File `.bak` là bản dự phòng khi thay thế nhật ký. Đường dẫn thuộc tài khoản Windows đang chạy ứng dụng; chạy dưới tài khoản khác sẽ dùng hồ sơ khác.

Ứng dụng ghi và flush bản chụp gốc trước khi di chuyển, ẩn hoặc bọc cửa sổ. Nếu không ghi được bản chụp, thao tác bảo vệ mới bị chặn và hiển thị lý do. Lỗi lưu trạng thái sau đó không làm mất bản chụp đã ghi. Khôi phục vẫn được phép nếu file tạm thời không ghi được; trường hợp đã trả cửa sổ nhưng chưa lưu kết quả được báo riêng và có thể thử ghi lại.

Ghi file dùng bản tạm và thay thế nguyên tử. Khi bản chính hỏng, có thể đọc bản dự phòng. Bản chính hỏng được giữ lại với đuôi `.corrupt-<id>` khi lưu lại; không dùng nó để ghi đè bản dự phòng còn tốt. Nếu cả hai không đọc được, ứng dụng giữ dữ liệu và chặn tạo bản ghi mới thay vì ghi đè bằng nhật ký trống. Sau khi xử lý quyền truy cập hoặc file lỗi, dùng **Kiểm tra lại**.

Trước khi bảo vệ thêm cửa sổ, lịch sử đã hoàn tất cũ được cắt bớt, giữ khoảng 200 mục gần nhất. Mục chưa khôi phục không bị loại âm thầm. Nhật ký có giới hạn 2.000 bản ghi và 4 MiB; vượt giới hạn sẽ báo lỗi lưu và chặn bảo vệ mới.

## Xác minh trước khi khôi phục

Bản ghi gắn với HWND gốc, PID, thời điểm tạo tiến trình, đường dẫn thực thi và Windows session. Một dấu riêng được gắn trực tiếp lên cửa sổ và cửa sổ cha/chủ sở hữu gốc nếu có. Việc tái sử dụng PID hoặc HWND không đủ để vượt qua kiểm tra này. Tiến trình hệ thống, tiến trình không xác minh được, phiên Windows khác và chính cửa sổ của Driver Booster bị loại theo quy tắc an toàn hiện có.

Khi khôi phục, ứng dụng kiểm tra lại định danh/dấu cửa sổ, trả quan hệ cha và chủ sở hữu, style, trạng thái topmost, desktop gốc, vị trí/kích thước và khả năng hiển thị. Không khởi chạy ứng dụng từ đường dẫn trong nhật ký. Nếu không xác minh được hoặc desktop gốc không còn, giữ bản ghi và thông báo để người dùng xử lý.

## Kiểm tra và giới hạn

Bộ kiểm tra C# đã qua **2.558 kiểm tra**, build với cảnh báo được xem là lỗi. Các ca mới bao gồm lưu trước thay đổi, tải lại sau sự cố, thử lại, lỗi ghi/đọc/khóa file, backup và JSON hỏng, sai PID/thời điểm tạo/session/đường dẫn/dấu HWND, thao tác khôi phục tất cả và đổi ngôn ngữ trên giao diện. Giao diện được dựng ở 860×600, 640×600 và 640×460.

Kiểm tra tích hợp Win32 chỉ thao tác HWND do bộ kiểm tra tạo, gồm gỡ cửa sổ khỏi host thử nghiệm và khôi phục chủ sở hữu popup. Một tiến trình ghi nhật ký riêng thực sự bị kết thúc cưỡng bức; phiên mới đọc nhật ký và hiện lại cửa sổ thử nghiệm còn tồn tại. Bộ kiểm tra không đóng ứng dụng người dùng, không chạy sự kiện Loaded của cửa sổ chính và không áp dụng tùy chọn thiết bị/firewall/hosts thực tế.

Khôi phục cửa sổ phụ thuộc cửa sổ gốc vẫn còn sống trong cùng Windows session. Đóng ứng dụng đích, khởi động lại Windows, hoặc hủy cửa sổ khi host bị crash có thể khiến cửa sổ không còn để khôi phục; màn hình sẽ báo trạng thái phù hợp khi kiểm tra. Tính năng này không thay thế autosave và không phục hồi nội dung chưa lưu. Nếu desktop gốc bị xóa hoặc quyền Windows không cho phép thay đổi cửa sổ, thao tác có thể thất bại và giữ thông tin cho lần thử tiếp theo.

Các bài kiểm tra tự động không chứng nhận mọi ứng dụng GPU, mọi mức DPI, thiết lập nhiều màn hình, thay đổi quyền hay mọi đường lỗi từ Windows. Cần kiểm tra trực tiếp thêm trong các môi trường đó nếu sử dụng; không có cam kết bao phủ mọi lỗi.

Đo line coverage cho các file mới: nhật ký 100%, dịch vụ khôi phục 98,95%, tích hợp màn hình 94,55%, mô hình 92,50%, lớp Win32 89,62%. Line coverage toàn ứng dụng là 35,17%; không đồng nghĩa toàn bộ nhánh hành vi hay môi trường thực tế đã được kiểm tra. Bộ kiểm tra trình duyệt hiện có cũng qua 78 kiểm tra Chrome sau thay đổi.

## Bản chạy đã cập nhật

Đã build từ cùng mã nguồn và đối chiếu SHA-256 với bản cài tại hai vị trí bắt buộc:

- `C:\Users\VirusBetaTester\Downloads\PalStatus\release`: Windows x64 độc lập, single-file.
- `C:\Users\VirusBetaTester\Downloads\PalStatus\StreamGuard\bin\Release\net8.0-windows`: bản đủ EXE, DLL, thông tin runtime và thư viện.

Các bản tại `win-x64` và `win-x64\publish` bên dưới thư mục runtime cũng được đồng bộ. Tổng cộng 484 file được đối chiếu; cấu hình người dùng không thay đổi. Cache build trong cây nguồn được chuyển vào Thùng rác sau khi hoàn tất kiểm tra.
