# Quy tắc cập nhật ứng dụng

Yêu cầu của người dùng: mỗi khi sửa ứng dụng hoặc thêm chức năng, phải cập nhật bản chạy ở **cả hai vị trí** sau:

1. `C:\Users\VirusBetaTester\Downloads\PalStatus\release`
2. `C:\Users\VirusBetaTester\Downloads\PalStatus\StreamGuard\bin\Release\net8.0-windows`

Tên ứng dụng và file chạy hiện tại là `Driver Booster` / `Driver Booster.exe`.

- Thư mục `release` nhận bản publish Windows x64 độc lập, đóng gói một file `.exe`.
- Thư mục `net8.0-windows` nhận bản chạy với đầy đủ `.exe`, `.dll`, `.deps.json`, `.runtimeconfig.json` và các thư viện phụ thuộc cần thiết. Chỉ chép `.exe` là không đủ khi mã ứng dụng trong `.dll` thay đổi.
- Hai bản phải được build từ cùng mã nguồn cuối cùng, kiểm tra phù hợp và đối chiếu file đã cài với bản build tương ứng. Không báo đã cập nhật đủ nếu một vị trí vẫn còn bản cũ.
- Nếu Windows khóa file do ứng dụng đang chạy, giữ bản build mới sẵn sàng và thông báo rõ vị trí còn chờ cập nhật. Tiếp tục thay file và kiểm tra sau khi ứng dụng đã thoát; không ghi đè DLL đang được sử dụng.
- Giữ cấu hình hiện có tại `%APPDATA%\StreamGuard` và danh sách ứng dụng/phím tắt của người dùng.
- Sau khi hoàn tất build và kiểm tra, dọn cache `obj`, `tests\obj`, `tests\bin` phát sinh trong cây mã nguồn vào Thùng rác; giữ mã nguồn kiểm tra và các bản chạy cần thiết.

Các thư mục chạy khác, nếu còn được sử dụng, cũng cần giữ nhất quán với hai vị trí bắt buộc trên.
