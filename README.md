# Driver Booster (StreamGuard)

> **Giải pháp bảo vệ quyền riêng tư màn hình, chống giám sát từ xa, bảo vệ lưu lượng AI & môi trường làm việc bảo mật toàn diện trên Windows**

Ứng dụng hiện hiển thị tên thương mại chính thức là **Driver Booster**; tệp thực thi chính là **`Driver Booster.exe`** (mã nguồn, namespace và cấu hình kế thừa từ nền tảng cốt lõi **`StreamGuard`**). Ứng dụng được thiết kế chuyên biệt để ngăn chặn toàn diện các hành vi quay lén, chụp trộm màn hình, theo dõi thao tác người dùng, can thiệp tiến trình, nghe lén lưu lượng AI và giám sát từ xa bởi các phần mềm bên ngoài (như RAIA, OBS, Discord, Zoom, Teams, TeamViewer, AnyDesk, Wireshark, Fiddler, Burp Suite...).

Logo chiếc khiên tím đặc trưng được nhúng trực tiếp vào tệp `.exe` và thanh tiêu đề ứng dụng, hỗ trợ hiển thị sắc nét trên Windows và Task Manager từ 16px đến 256px (`Assets/AppIcon.ico` và vector gốc `Assets/AppIcon.svg`).

---

## Mục lục

1. [Yêu cầu hệ thống & Độ tương thích](#1-yêu-cầu-hệ-thống--độ-tương-thích)
2. [Hai vị trí chạy chính thức](#2-hai-vị-trí-chạy-chính-thức)
3. [Giao diện & Thiết kế thanh bên (Sidebar)](#3-giao-diện--thiết-kế-thanh-bên-sidebar)
4. [Đa ngôn ngữ (Language Support)](#4-đa-ngôn-ngữ-language-support)
5. [Chi tiết 7 Phân hệ chức năng chính](#5-chi-tiết-7-phân-hệ-chức-năng-chính)
   - [5.1 Bảng bảo vệ (Shield Dashboard)](#51-bảng-bảo-vệ-shield-dashboard)
   - [5.2 Ứng dụng khả dụng (Running Explorer)](#52-ứng-dụng-khả-dụng-running-explorer)
   - [5.3 Mối đe dọa (Threat Shield & Dual-Engine Firewall)](#53-mối-đe-dọa-threat-shield--dual-engine-firewall)
   - [5.4 Riêng tư & Bảo mật (Privacy & Security)](#54-riêng-tư--bảo-mật-privacy--security)
     - [Bảo vệ lưu lượng AI & Chống soi gói tin (AI Traffic Shield)](#bảo-vệ-lưu-lượng-ai--chống-soi-gói-tin-ai-traffic-shield)
     - [Tích hợp bộ cài đặt Antigravity Desktop Offline](#tích-hợp-bộ-cài-đặt-antigravity-desktop-offline)
     - [Cầu nối đăng nhập Antigravity tự động & bảo mật (Antigravity Secure Login Bridge)](#cầu-nối-đăng-nhập-antigravity-tự-động--bảo-mật-antigravity-secure-login-bridge)
     - [Khóa công cụ quản trị hệ thống (System Tools Lock)](#khóa-công-cụ-quản-trị-hệ-thống-system-tools-lock)
     - [Khiên con trỏ chuột (Cursor Shield / Ghost Cursor)](#khiên-con-trỏ-chuột-cursor-shield--ghost-cursor)
     - [Mở khóa sao chép & dán trang web (Copy & Paste Unlocker)](#mở-khóa-sao-chép--dán-trang-web-copy--paste-unlocker)
     - [Chụp màn hình bảo mật (Secure Snipping Tool)](#chụp-màn-hình-bảo-mật-secure-snipping-tool)
   - [5.5 Trung tâm khôi phục (Recovery Center)](#55-trung-tâm-khôi-phục-recovery-center)
   - [5.6 Phụ đề trực tiếp (Live Captions)](#56-phụ-đề-trực-tiếp-live-captions)
   - [5.7 Trình duyệt bảo vệ (Protected Chromium Browser - Local Sandbox)](#57-trình-duyệt-bảo-vệ-protected-chromium-browser---local-sandbox)
6. [Bảng phím tắt toàn cầu (Global Hotkeys)](#6-bảng-phím-tắt-toàn-cầu-global-hotkeys)
7. [Cơ chế an toàn & Cứu hộ khẩn cấp](#7-cơ-chế-an-toàn--cứu-hộ-khẩn-cấp)
8. [Cấu trúc thư mục dữ liệu & Cấu hình](#8-cấu-trúc-thư-mục-dữ-liệu--cấu-hình)
9. [Hướng dẫn Build & Đóng gói](#9-hướng-dẫn-build--đóng-gói)
10. [Kiểm thử tự động (Automated Test Suite)](#10-kiểm-thử-tự-động-automated-test-suite)
11. [Bản quyền & Giấy phép](#11-bản-quyền--giấy-phép)

---

## 1. Yêu cầu hệ thống & Độ tương thích

- **Hệ điều hành**: Windows 10 (bản 1607 trở lên) hoặc Windows 11 (21H2, 22H2, 23H2, 24H2) 64-bit.
- **Nền tảng**: .NET 8.0 Windows Desktop Runtime x64.
- **DPI Scaling**: Hỗ trợ đầy đủ màn hình đa độ phân giải và High-DPI (`PerMonitorV2`), từ Full HD đến 2K/4K (tỷ lệ co giãn 100%, 125%, 150%, 175%, 200%).
- **Trình duyệt nhúng**: Microsoft Edge WebView2 Runtime (được tích hợp sẵn mặc định trên Windows 11 và hầu hết Windows 10; **hoàn toàn không yêu cầu phải cài Google Chrome**).
- **Quyền hạn**:
  - Không bắt buộc quyền Administrator cho các tác vụ ẩn cửa sổ, phụ đề, bắt đăng nhập và trình duyệt bảo vệ.
  - Khuyến nghị chạy với quyền Administrator (`Run as administrator`) để kích hoạt đầy đủ Tường lửa dự phòng kép (`Dual-Engine Firewall`), chỉnh sửa hosts cách ly và khóa cổng phần cứng Camera/Microphone.

---

## 2. Hai vị trí chạy chính thức

Theo quy chuẩn cập nhật bắt buộc tại [AGENTS.md](file:///c:/Users/VirusBetaTester/Downloads/PalStatus/StreamGuard/AGENTS.md), mỗi khi build hoặc nâng cấp chức năng, ứng dụng luôn được cập nhật đồng bộ tại **cả hai vị trí**:

1. **Bản Publish Độc Lập (Single-File Standalone)**:
   - Thư mục: [`C:\Users\VirusBetaTester\Downloads\PalStatus\release`](file:///C:/Users/VirusBetaTester/Downloads/PalStatus/release)
   - Tệp chạy: `Driver Booster.exe` (~406 MB). Đã đóng gói toàn bộ .NET runtime, WPF, thư viện native, font và tài nguyên trong một tệp duy nhất, có thể chạy ngay trên máy mới mà không cần cài thêm .NET.
   - Thư mục kèm theo: `installers\AntigravitySetup.exe` (~199.6 MB) phục vụ cài đặt offline.
2. **Bản Build Đầy Đủ Thư Viện (Unpacked Assembly)**:
   - Thư mục: [`C:\Users\VirusBetaTester\Downloads\PalStatus\StreamGuard\bin\Release\net8.0-windows`](file:///C:/Users/VirusBetaTester/Downloads/PalStatus/StreamGuard/bin/Release/net8.0-windows)
   - Tệp chạy: `Driver Booster.exe` và `Driver Booster.dll`, đi kèm các tệp `.deps.json`, `.runtimeconfig.json`, runtimes nền tảng và `installers\AntigravitySetup.exe`.

> [!IMPORTANT]
> Toàn bộ quy trình phát hành tuyệt đối **không tự động nén thành tệp `.zip`**. Bản chạy mới luôn được kiểm tra và ghi đè an toàn khi không bị hệ thống khóa.

---

## 3. Giao diện & Thiết kế thanh bên (Sidebar)

- **Bộ biểu tượng vector đồng bộ**: Sử dụng toàn bộ icon vector sắc nét, tự đổi màu theo trạng thái (Active / Inactive / Warning / Muted).
- **Độ co giãn thông minh (Responsive Sidebar)**:
  - Rê chuột và kéo mép phải của thanh bên để điều chỉnh bề rộng tùy ý từ **200 px đến 300 px**.
  - Kéo về dưới 160 px để thu gọn thành dạng mini **60 px** (chỉ hiển thị biểu tượng, tối ưu không gian làm việc).
  - Cửa sổ tự động thu gọn thanh bên khi kích thước cửa sổ hẹp và tự phục hồi kích thước ưa thích khi mở rộng cửa sổ.
  - Nút biểu tượng Hamburger (☰) hỗ trợ bật/tắt thu gọn nhanh.
- **Hỗ trợ bàn phím đầy đủ**: Dùng phím `Tab` để di chuyển đến vạch điều chỉnh, phím `Mũi tên Trái / Phải` để thay đổi độ rộng, `Home` để thu gọn về icon, `End` để mở rộng tối đa, và `Esc` để hủy thao tác kéo.
- **Giao diện sạch (Clean UI)**: Không gây phiền toái bởi các tooltip nổi che khuất tầm nhìn, không hiện balloon notification khó chịu ở khay hệ thống.

---

## 4. Đa ngôn ngữ (Language Support)

- Hỗ trợ song ngữ hoàn chỉnh: **Tiếng Việt (`vi-VN`)** và **English (`en-US`)**.
- **Chuyển đổi tức thì**: Có thể đổi ngôn ngữ ngay trong ứng dụng qua phím tắt **F4** hoặc vào mục cài đặt. Toàn bộ chuỗi giao diện, tiêu đề, trạng thái, hộp thoại và thông báo banner được cập nhật ngay lập tức mà không cần khởi động lại.
- **Độc lập dữ liệu**: Việc đổi ngôn ngữ chỉ tác động đến lớp hiển thị; toàn bộ danh sách tiến trình, cấu hình bảo vệ, trạng thái phím tắt và nhật ký khôi phục được giữ nguyên vẹn.
- Tùy chọn ngôn ngữ được lưu tự động trong trường `Language` của `%APPDATA%\StreamGuard\config.json`.

---

## 5. Chi tiết 7 Phân hệ chức năng chính

### 5.1 Bảng bảo vệ (Shield Dashboard)
- **Quản lý danh sách ứng dụng được bảo vệ**: Hiển thị trực quan từng cửa sổ đang được ẩn/bảo vệ, biểu tượng ứng dụng, PID và chế độ hoạt động.
- **Cơ chế ẩn màn hình ảo (Virtual Desktop Cloak)**:
  - Tự động di chuyển ứng dụng được bảo vệ sang một Virtual Desktop độc lập (`IVirtualDesktopManager::MoveWindowToDesktop`).
  - Ứng dụng biến mất hoàn toàn khỏi Desktop 1 (màn hình đang chia sẻ/livestream) nhưng vẫn tiếp tục chạy mượt mà ở Desktop 2.
  - Người dùng có thể dùng tổ hợp phím `Win + Ctrl + Mũi tên Trái/Phải` để chuyển qua lại làm việc khi cần.
- **Cơ chế dự phòng tự động (Automatic Fallback)**:
  - Nếu Windows không hỗ trợ Virtual Desktop COM hoặc xảy ra lỗi môi trường, hệ thống tự động chuyển sang chế độ **WDA In-place Wrapper** (`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`) hoặc Win32 Hide để đảm bảo ứng dụng không bao giờ bị văng hoặc gián đoạn.
- **Nút Khẩn Cấp (Panic Restore)**: Bấm nút `Panic: Khôi phục tất cả` hoặc phím tắt `Ctrl+Shift+Alt+H` để đưa toàn bộ ứng dụng trở về màn hình chính ngay tức khắc.

### 5.2 Ứng dụng khả dụng (Running Explorer)
- Quét và liệt kê danh sách toàn bộ các cửa sổ và tiến trình ứng dụng đang mở trên máy tính.
- **Bộ lọc an toàn tiến trình hệ thống (Process Safety Filter)**:
  - Tự động nhận diện và ẩn đi các tiến trình cốt lõi của Windows, thành phần `SystemApps`, `servicing`, thư mục `System32`/`SysWOW64`, các dịch vụ Session 0 và tiến trình đánh dấu Critical.
  - Ngăn ngừa hoàn toàn nguy cơ vô tình can thiệp làm treo hoặc hỏng hệ điều hành. Các ứng dụng văn phòng, trình duyệt, Notepad, Calculator, Paint... vẫn được nhận diện bình thường.
- **Thao tác đơn giản**: Chọn ứng dụng từ danh sách và bấm `+ Add` hoặc kéo thả trực tiếp tệp `.exe` vào cửa sổ để bắt đầu quản lý.

### 5.3 Mối đe dọa (Threat Shield & Dual-Engine Firewall)
- **Nhận diện 33+ nhóm công cụ giám sát**: Quét theo thời gian thực các phần mềm ghi hình, chụp ảnh, họp trực tuyến và điều khiển từ xa (OBS Studio, Discord, Camtasia, Bandicam, Zoom, Microsoft Teams, TeamViewer, AnyDesk, v.v.).
- **Rèm che bảo mật tự động (Privacy Curtain)**:
  - Tự động phủ màn che đen bảo vệ trên toàn bộ các màn hình ngay khi phát hiện tiến trình giám sát hoặc khi người dùng kích hoạt thủ công.
  - Phím tắt `Ctrl+Shift+Alt+C` cho phép gỡ và tạm dừng rèm che khẩn cấp.
- **Tường lửa dự phòng kép (Dual-Engine Firewall)**:
  - Kết hợp hai tầng thực thi: PowerShell script và lệnh hệ thống native `netsh advfirewall`.
  - Trong các môi trường bị hạn chế PowerShell (bởi chính sách AppLocker, WDAC hoặc Constrained Language Mode), hệ thống tự động chuyển sang engine native `netsh` để cách ly và chặn kết nối ra ngoài của tiến trình nghi vấn.
- **Khóa thiết bị phần cứng**: Hỗ trợ ngắt quyền truy cập Microphone và Camera ở cấp hệ thống để chống nghe lén/ghi hình bí mật.
- **Điều tiết chu kỳ quét (Adaptive Polling)**:
  - Tự động giãn chu kỳ quét lên 5 giây khi ứng dụng thu nhỏ xuống khay hệ thống (giảm tải CPU xuống mức gần 0% và tiết kiệm pin laptop).
  - Tự động tăng tốc chu kỳ quét về 2 giây khi phát hiện nguy cơ hoặc khi mở lại cửa sổ.

---

### 5.4 Riêng tư & Bảo mật (Privacy & Security)

#### Bảo vệ lưu lượng AI & Chống soi gói tin (AI Traffic Shield)
Phân hệ chuyên dụng bảo vệ toàn vẹn nội dung câu hỏi (prompts) và câu trả lời (responses) khi người dùng tương tác với các mô hình AI (như Antigravity, Gemini, Antigravity IDE, ChatGPT...):
- **Cơ chế Tự động triệt tiêu Proxy (Auto-Purge Rogue Loopback Proxy)**:
  - Khi phát hiện bất kỳ phần mềm bắt gói tin nào (Fiddler, Charles Proxy, mitmproxy, Proxyman, Burp Suite, v.v.) tự động bật cờ Proxy loopback (`127.0.0.1:8888`, `127.0.0.1:8080`, `localhost`), Driver Booster lập tức can thiệp xóa sạch thiết lập proxy trong Registry Windows (`Software\Microsoft\Windows\CurrentVersion\Internet Settings`), khôi phục kết nối mạng trực tiếp (Direct Connection) để ngăn chặn rò rỉ dữ liệu AI.
- **Quét chứng chỉ Root CA giả mạo MITM**: Tự động rà soát kho chứng chỉ gốc đáng tin cậy của Windows (`LocalMachine\Root` và `CurrentUser\Root`) để phát hiện và cảnh báo các chứng chỉ giả mạo được các công cụ phân tích cài lén nhằm giải mã HTTPS.
- **Giám sát công cụ Sniffer mạng**: Theo dõi sự hiện diện của Wireshark, Process Hacker, HTTP Debugger Pro, v.v.
- **Bảo vệ tệp hosts cho AI APIs**: Giám sát file `C:\Windows\System32\drivers\etc\hosts`, ngăn chặn hành vi giả mạo DNS hoặc chuyển hướng các endpoint AI chính thức của Google (`generativelanguage.googleapis.com`, `gemini.google.com`, `aiplatform.googleapis.com`).

#### Tích hợp bộ cài đặt Antigravity Desktop Offline
- **Hoàn toàn độc lập & Offline 100%**: Đi kèm sẵn tệp cài đặt chính thức `AntigravitySetup.exe` (~199.6 MB) trong thư mục `installers\` của cả hai bản build. Bạn có thể mang toàn bộ thư mục sang bất kỳ máy tính mới nào không có kết nối Internet và cài đặt được ngay.
- **Linh hoạt giữa 2 chế độ**:
  - *Cài đặt trực quan (Standard Interactive)*: Mở giao diện cài đặt chính thức của Google để theo dõi tiến trình.
  - *Cài đặt ngầm (Silent Install)*: Tự động phát hiện bộ đóng gói (tương thích cả Inno Setup `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` lẫn NSIS `/S`) để cài đặt chạy nền hoàn toàn êm ái.
- **Nhận diện chính xác**: Phân biệt triệt để Antigravity Desktop (Agentic AI) với bản Antigravity IDE và CLI (`agy`).
- **Quản trị thông minh**: Hiển thị badge trạng thái động (Đã cài đặt [xanh] / Chưa cài đặt [vàng]), nút "Khởi chạy Antigravity" an toàn (chỉ kích hoạt khi tệp thực thi tồn tại thực tế trên ổ đĩa), và công tắc bật/tắt chế độ Không làm phiền (DND) chặn thông báo sự kiện đặc biệt và âm báo.

#### Cầu nối đăng nhập Antigravity tự động & bảo mật (Antigravity Secure Login Bridge)
- **Giải quyết triệt để vấn đề đăng nhập AI**: Khi Antigravity Desktop mở luồng đăng nhập tài khoản Google, theo mặc định hệ thống sẽ mở trình duyệt bên ngoài hệ điều hành (vốn không được che chắn chống chụp màn hình). Cầu nối của StreamGuard giúp bắt và đưa toàn bộ luồng đăng nhập vào **Trình duyệt bảo vệ (Protected Browser)** hoàn toàn tự động.
- **Bắt liên kết đa kênh đồng thời**:
  - *Kênh Process Watcher (WMI)*: Lắng nghe sự kiện tạo tiến trình `Win32_ProcessStartTrace` của các trình duyệt phổ biến (`chrome.exe`, `msedge.exe`, `brave.exe`, `firefox.exe`, `opera.exe`...). Khi phát hiện tiến trình mang tham số dòng lệnh URL `accounts.google.com`, hệ thống tự động tắt (terminate) trình duyệt ngoài ngay lập tức để ngăn lộ thông tin, đồng thời trích xuất URL đưa vào Trình duyệt bảo vệ.
  - *Kênh Clipboard Polling*: Tự động rà soát an toàn nội dung khay nhớ tạm với cơ chế chống xung đột khóa clipboard (`0x800401D0` - `CLIPBRD_E_CANT_OPEN`), bắt tức thì liên kết Google OAuth nếu người dùng bấm sao chép link trên Antigravity.
- **Tự động chuyển hướng & Đồng bộ phiên Loopback**:
  - Ngay khi bắt được URL, ứng dụng lập tức chuyển sang Tab Trình duyệt bảo vệ và tải trang đăng nhập Google trong môi trường tàng hình (`WDA_EXCLUDEFROMCAPTURE`).
  - Sau khi đăng nhập thành công, Google điều hướng về cổng Loopback của Antigravity (`http://127.0.0.1:<port>/?code=...` hoặc `http://localhost:<port>/?code=...`). Trình duyệt bảo vệ tự động phát hiện mã xác thực thành công và hiển thị thông báo đồng bộ phiên làm việc.
- **Cơ chế an toàn & Kiểm tra điều kiện**:
  - Kiểm tra xem Antigravity đã được cài đặt hay chưa trước khi chờ đăng nhập. Nếu chưa cài đặt, hiển thị cảnh báo `AgyBridge.NotInstalled` hướng dẫn người dùng thay vì chờ đợi vô ích.
  - Sử dụng biểu thức chính quy khắt khe (chỉ chấp nhận domain Google OAuth chính thống, từ chối URL độc hại hoặc script injection).
  - Tự động huỷ lắng nghe sau 90 giây timeout để tiết kiệm tài nguyên.

#### Khóa công cụ quản trị hệ thống (System Tools Lock)
- Khóa truy cập vào **Task Manager** (`taskmgr.exe`), **Registry Editor** (`regedit.exe`), **Command Prompt** (`cmd.exe`) và **Group Policy Editor** (`gpedit.msc`).
- Ngăn chặn người khác hoặc phần mềm độc hại đóng Driver Booster hoặc vô hiệu hóa các lớp bảo vệ.
- Tự động mở khóa an toàn khi ứng dụng thoát bình thường.

#### Khiên con trỏ chuột & Tàng hình chuột (Cursor Shield & Cursor Cloak)
- **Khiên con trỏ chuột (`Cursor Shield`)**: Chuẩn hóa hình dạng con trỏ chuột hệ thống khi bảo vệ ứng dụng, ngăn lộ trạng thái con trỏ khi quay/chụp màn hình.
- **Tàng hình chuột trên ứng dụng (`In-App Cursor Cloak`)**: Ẩn con trỏ thật bên trong cửa sổ `Driver Booster` (`WM_SETCURSOR` / `Cursors.None`) và hiển thị con trỏ giả lập (`VisualCursorAdorner`) chỉ cho người dùng cục bộ.
- **Tàng hình chuột trên toàn màn hình (`Global Cursor Cloak` — `Ctrl + Shift + M`)**:
  - Thay thế toàn bộ con trỏ hệ thống (`SystemCursorIds`) bằng con trỏ trong suốt để ẩn hoàn toàn con trỏ chuột thật khi chia sẻ toàn màn hình (Google Meet, OBS, Zoom...), đồng thời vẽ con trỏ giả lập cục bộ trên `GlobalCursorOverlayWindow` (`WDA_EXCLUDEFROMCAPTURE`, tự giữ `HWND_TOPMOST` trên cửa sổ Fullscreen).
  - Phối hợp an toàn với `CursorShieldService` (không khôi phục nhầm con trỏ mặc định khi đang bật `Global Cursor Cloak`) và hỗ trợ phím khẩn cấp **`Ctrl + Shift + Esc`** để khôi phục con trỏ Windows ngay lập tức.

#### Mở khóa sao chép & dán trang web (Copy & Paste Unlocker)
- Cung cấp các script mở khóa nhúng sẵn (`BrowserScripts/EnableCopyPaste.js`) để khôi phục thao tác chuột phải, bôi đen văn bản, và tổ hợp phím `Ctrl+C / Ctrl+V / Ctrl+X` trên các trang web có tính năng ngăn chặn.
- Hỗ trợ 3 phương thức: **Sao chép Script Console F12**, **Sao chép Bookmarklet**, và **Sao chép lệnh hoàn nguyên**.

#### Chụp màn hình bảo mật (Secure Snipping Tool)
- Phím tắt mặc định **`Ctrl + Alt + S`** (hoặc tùy chỉnh trong cài đặt) cho phép khoanh vùng và chụp ảnh màn hình nhanh.
- Hoạt động an toàn trên **Chrome Fullscreen** có giám sát: `SnippingOverlayWindow` dùng `WS_EX_NOACTIVATE` + `MA_NOACTIVATE` (không làm Chrome mất focus `blur` / `visibilitychange`), `HWND_TOPMOST` phủ trên Fullscreen, `WDA_EXCLUDEFROMCAPTURE` (ẩn lớp phủ khỏi quay/chụp màn hình), và hook bàn phím `WH_KEYBOARD_LL` chặn phím `Escape` để hủy chụp mà không làm Chrome thoát Fullscreen.
- Tùy chọn tự động sao chép ảnh vào Clipboard hoặc lưu thành tệp hình ảnh vào thư mục cấu hình riêng.

#### An toàn thao tác & Vùng nguy hiểm (Passive Focus, Switch Cooldown & Confirmation)
- **Passive Focus Mode**: Giữ cửa sổ `Driver Booster` ở chế độ không cướp focus khi người dùng thao tác song song với trình duyệt/Chrome.
- **Chống bật/tắt liên tục Switch (`SwitchCooldownService`)**: Áp dụng thời gian chờ `1.2 giây` độc lập cho từng switch chức năng (`CheckBox` / `ToggleButton`) khi bấm chuột hoặc nhấn `Space`, kèm thông báo cảnh báo `ToastType.Warning` để tránh xung đột hệ thống.
- **Xác nhận Vùng nguy hiểm**: Các thao tác đặt lại gốc (*Factory Reset & Wipe Data*) và gỡ cài đặt hoàn toàn (*Complete Clean Uninstall*) đều có hộp thoại xác nhận trước khi thực thi.

#### Khiên khóa phím tắt Desktop ảo (Virtual Desktop Hotkeys Shield)
- Cung cấp công tắc Switch bật/tắt tiện lợi trong cả hai phân hệ **Quyền riêng tư & Cách ly** và **Cài đặt hệ thống** (đồng bộ trạng thái 100%).
- Ngăn ngừa hoàn toàn nguy cơ vô tình bấm nhầm các tổ hợp phím Desktop ảo của Windows làm xáo trộn màn hình hoặc để lộ cửa sổ bảo mật:
  - **`Win + Ctrl + D`**: Chặn tạo mới Desktop ảo.
  - **`Win + Ctrl + F4`**: Chặn đóng Desktop ảo.
  - **`Win + Ctrl + Mũi tên Trái / Phải`**: Chặn chuyển đổi qua lại giữa các Desktop ảo bằng phím tắt.
  - **`Win + Tab`**: Chặn mở giao diện tổng quan Task View (ngăn hiển thị thumbnail cửa sổ và danh sách desktop).
- Sử dụng hook bàn phím mức thấp (`WH_KEYBOARD_LL`) để lọc chuẩn xác ở cấp hệ thống mà không ảnh hưởng đến các phím bình thường như `Win+D` (Hiển thị Desktop), `Alt+Tab` hay `Ctrl+Tab`.
- Tự động hiển thị thông báo Toast mỗi khi phát hiện và chặn thành công tổ hợp phím desktop ảo.

---

### 5.5 Trung tâm khôi phục (Recovery Center)
- **Nhật ký khôi phục độc lập**: Toàn bộ trạng thái nguyên bản của cửa sổ trước khi bảo vệ được lưu riêng tại `%APPDATA%\StreamGuard\config.recovery.json`, tách biệt khỏi tệp cấu hình chính.
- **Bảo toàn khi gặp sự cố đột ngột**: Nếu máy tính bị sập nguồn hoặc tắt app đột ngột, khi mở lại ứng dụng, Trung tâm khôi phục sẽ liệt kê đầy đủ các cửa sổ chưa hoàn tất.
- **Hỗ trợ thao tác**: Bấm *Thử khôi phục lại* cho từng mục, *Khôi phục tất cả* để đưa mọi cửa sổ về vị trí ban đầu, và *Kiểm tra lại* để xác thực trạng thái thực tế của HWND trên Windows.

---

### 5.6 Phụ đề trực tiếp & Dịch thời gian thực (Live Captions & Real-time Translation)
- **Tích hợp sẵn Speech-to-Text**: Nhận dạng giọng nói tiếng Anh theo thời gian thực từ âm thanh phát ra loa hoặc tai nghe (Loopback Audio Capture).
- **Hoạt động hoàn toàn Offline**: Đã nhúng sẵn mô hình Whisper int8 và runtime Python biệt lập; không cần cài đặt Python hay thư viện ngoài.
- **Quản lý bộ nhớ MKL tối ưu**: Tự động giới hạn luồng Intel MKL và OpenMP (`MKL_NUM_THREADS = 1`, `OMP_NUM_THREADS = 2`), giúp nhận dạng mượt mà, triệt tiêu nguy cơ tràn bộ nhớ scratchpad trên CPU nhiều luồng.
- **Dịch Phụ đề Thời gian thực sang Tiếng Việt (Real-time Translation)**:
  - Tự động dịch các đoạn phụ đề tiếng Anh sang Tiếng Việt với độ trễ cực thấp (<200ms).
  - **Chế độ Song ngữ (Bilingual)**: Hiển thị dòng trên là câu tiếng Anh gốc (chữ màu xám xanh dịu mắt), dòng dưới là Tiếng Việt dịch chuẩn (chữ màu trắng sáng nổi bật), cực kỳ hữu ích khi nghe họp trực tuyến, hội thảo quốc tế hoặc video tiếng Anh.
  - **Chế độ Chỉ Tiếng Việt (Vietnamese Only)**: Cho phép chuyển đổi linh hoạt sang hiển thị hoàn toàn bản dịch tiếng Việt.
  - **Phím tắt nhanh & Nút bấm trên cửa sổ nổi**: Tổ hợp phím toàn cầu **`Ctrl + Alt + T`** hoặc nút bấm **`VI`** ngay trên thanh công cụ của cửa sổ nổi giúp bật/tắt dịch tức thì mọi lúc mọi nơi.
  - **Bộ đệm thông minh (LRU Cache)**: Bộ nhớ đệm tốc độ cao lưu trữ đến 2,000 cụm từ thường gặp, cho thời gian phản hồi tức thì (0ms) và tiết kiệm lưu lượng.
  - **Bản chép lời Song ngữ, Định dạng `TOEIC (A-D)` & Ghi tức thời (Real-time AutoFlush)**:
    - Tự động lưu cả câu gốc tiếng Anh và bản dịch Tiếng Việt vào tệp `.txt` và `.srt` theo mốc thời gian tại `%APPDATA%\StreamGuard\Captions\transcripts\`. Cơ chế streaming I/O `AutoFlush = true` ghi đĩa tức thì (<1ms).
    - Hỗ trợ 3 chế độ hiển thị trong hộp xem bản chép lời (**`TXT | SRT | TOEIC (A-D)`**): riêng chế độ **`TOEIC (A-D)`** tự động định dạng câu hỏi/ngữ cảnh và tách 4 lựa chọn `(A)`, `(B)`, `(C)`, `(D)` rõ ràng, kèm bản dịch thụt lề ngay dưới từng câu hỏi và đáp án khi bật dịch song ngữ.
  - **Thao tác nhanh trên Bản chép lời đã lưu (Transcript Quick Actions)**:
    - **Nút "Sao chép nhanh" (Quick Copy)**: Sao chép nhanh toàn bộ nội dung bản chép lời vào bộ nhớ tạm (Clipboard), hỗ trợ đọc toàn bộ tệp hoàn chỉnh kể cả khi phiên ghi âm dài vượt ngưỡng hiển thị 256 KB, kèm hiệu ứng đổi biểu tượng tích xanh ("Đã sao chép!") tức thì 1.5 giây.
    - **Nút "Xoá nhanh" (Quick Clear)**: Xoá nhanh toàn bộ nội dung trong khu vực bản chép lời (đối với phiên đã lưu: xoá sạch tệp phiên trên đĩa và làm mới danh sách; đối với phiên đang ghi trực tiếp: làm sạch khung hiển thị và ngắt tự cuộn để tránh rối mắt).
    - **Nút "Xoá các phiên trước" (Delete Previous Sessions)**: Nút công cụ kèm biểu tượng thùng rác vector cho phép xoá một lượt các phiên chép lời cũ trước đó (giữ lại phiên mới nhất hoặc phiên đang ghi âm), chuyển an toàn vào Thùng rác Windows.
    - **Nút xoá phiên riêng lẻ**: Biểu tượng thùng rác đặt ngay cạnh danh sách chọn phiên chép lời, hỗ trợ xoá từng phiên cũ một cách thuận tiện.
    - **Nút "Đóng phần xem" kèm Icon**: Đồng bộ hóa toàn bộ thanh công cụ với biểu tượng đóng vector sắc nét.
  - **Từ điển từ khóa chuyên ngành & Bảo toàn thuật ngữ (Smart Vocabulary & Jargon Guard)**:
    - Cho phép người dùng nhập danh sách từ khó, tên riêng, thuật ngữ kỹ thuật IT (cách nhau bởi dấu phẩy) ngay trên giao diện cài đặt phụ đề.
    - Tự động nắn chỉnh và chuẩn hóa các lỗi nhận diện âm học tương tự của mô hình Whisper (ví dụ: *anti gravity* &rarr; **Antigravity**, *jiminy* &rarr; **Gemini**, *c-sharp* &rarr; **C#**, *dot net* &rarr; **.NET**, *driver booster* &rarr; **Driver Booster**, *stream guard* &rarr; **StreamGuard**).
    - Bảo toàn thuật ngữ gốc khi dịch sang tiếng Việt, triệt tiêu tình trạng dịch ngô nghê theo nghĩa đen (ví dụ: không dịch *Driver Booster* thành *"bộ tăng áp trình điều khiển"*, không dịch *Antigravity* thành *"chống trọng lực"*).
- **Cửa sổ phụ đề nổi (Floating Overlay)**:
  - Chống chụp/quay màn hình (`WDA_EXCLUDEFROMCAPTURE`).
  - Hỗ trợ nhấp xuyên (Click-through) để không làm cản trở thao tác chơi game hoặc làm việc.
  - Tùy chỉnh kích thước chữ (`A− / A+`), chế độ hiển thị 1 dòng hoặc 2 dòng gần nhất.

---

### 5.7 Trình duyệt bảo vệ (Protected Chromium Browser - Local Sandbox)
Phân hệ trình duyệt bảo mật được tích hợp trực tiếp thành một Tab riêng trên thanh bên:
- **Chạy hoàn toàn độc lập - KHÔNG cần cài Google Chrome**:
  - Sử dụng nhân **Microsoft Edge WebView2** (dựa trên nền tảng Chromium).
  - Tích hợp sẵn mặc định trên Windows 11 và hầu hết bản cài Windows 10 hiện đại.
  - Người dùng có thể lướt web, tra cứu tài liệu và đăng nhập tài khoản an toàn ngay cả khi máy tính hoàn toàn không cài đặt Google Chrome.
- **Kháng ghi hình toàn diện (Anti-Screen-Capture)**:
  - Cửa sổ trình duyệt nằm trọn trong kiến trúc bảo vệ tàng hình (`WDA_EXCLUDEFROMCAPTURE`), hoàn toàn vô hình trước các phần mềm quay màn hình hay chụp trộm (OBS, Discord, Zoom, Teams, RAIA...).
- **Hộp cát dữ liệu riêng biệt (Isolated Sandbox Profile)**:
  - Toàn bộ Cookie, lịch sử duyệt web, bộ nhớ đệm (Cache) và phiên đăng nhập được lưu trữ độc lập tại:  
    `%LOCALAPPDATA%\StreamGuard\BrowserProfile`
  - Hoàn toàn tách biệt, không dùng chung và không đọc trộm dữ liệu từ Google Chrome hoặc Microsoft Edge bên ngoài hệ thống.
- **Omnibox tìm kiếm thông minh**: Thanh địa chỉ tự động nhận diện URL hoặc từ khóa tìm kiếm để truy vấn trực tiếp qua Google Search (`https://www.google.com`), hỗ trợ phím `Enter` để điều hướng và `Esc` để hủy.
- **Điều khiển điều hướng hoàn chỉnh**: Các nút Quay lại (Back), Đi tiếp (Forward), Tải lại (Refresh), Trang chủ (Home), và nút mở DevTools (F12).
- **Bộ công cụ tăng cường tích hợp sẵn**:
  - **Mở khóa Copy / Paste tự động**: Gỡ bỏ giới hạn bôi đen văn bản, chuột phải và tổ hợp phím sao chép trên các trang web có tính năng ngăn chặn.
  - **Cơ chế Chống mờ / Khóa tiêu điểm & Fullscreen (Anti-Blur & F11 Fullscreen Lock)**: Ghi đè `document.hidden = false`, `document.visibilityState = 'visible'`, `document.hasFocus() = true`, đồng thời khóa các sự kiện `blur`, `visibilitychange`, `fullscreenchange` và `exitFullscreen()` để ngăn trang web phát hiện chuyển tab hoặc thoát fullscreen; hỗ trợ nút **Khóa Focus Chrome** sao chép nhanh script Console/Bookmarklet cho Chrome ngoài.
  - **Phím tắt F12 DevTools**: Bật/tắt giao diện nhà phát triển để soi mã nguồn, phân tích mạng và gỡ lỗi web.
  - **Nút "⚡ Đăng nhập Antigravity (Tự động)"**: Tự động kích hoạt Antigravity và đón bắt liên kết đăng nhập Google để xác thực an toàn ngay trong tab.
  - **Nút "Mở bằng Chrome ngoài"**: Nếu người dùng muốn chuyển trang web ra ngoài, ứng dụng tự tìm `chrome.exe` để mở; nếu máy không có Chrome, ứng dụng tự động mở bằng trình duyệt mặc định sẵn có của hệ thống kèm thông báo hướng dẫn.

---

## 6. Bảng phím tắt toàn cầu (Global Hotkeys)

| Phím tắt | Chức năng | Phạm vi |
| :--- | :--- | :--- |
| **`Ctrl + Shift + G`** hoặc **`Shift + S + G`** | Hiện hoặc ẩn cửa sổ Driver Booster | Toàn cầu (Global) |
| **`Ctrl + Shift + Alt + H`** | **Panic Restore**: Khôi phục khẩn cấp toàn bộ cửa sổ về Desktop 1 | Toàn cầu (Global) |
| **`Ctrl + Shift + Alt + C`** | Gỡ hoặc tạm dừng Rèm che bảo vệ (Privacy Curtain) | Toàn cầu (Global) |
| **`Ctrl + Shift + M`** | Bật hoặc tắt **Tàng hình chuột trên toàn màn hình** (`Global Cursor Cloak`) | Toàn cầu (Global) |
| **`Ctrl + Shift + Esc`** | Khôi phục khẩn cấp con trỏ chuột Windows mặc định | Toàn cầu (Global) |
| **`Ctrl + Alt + S`** (hoặc cấu hình) | Kích hoạt công cụ Chụp ảnh màn hình bảo mật | Toàn cầu (Global) |
| **`Ctrl + Alt + T`** | Bật hoặc tắt Dịch phụ đề thời gian thực sang Tiếng Việt | Toàn cầu (Global) |
| **`F4`** | Mở bộ chọn ngôn ngữ hiển thị (Tiếng Việt / English) | Trong ứng dụng |
| **`F12`** | Mở công cụ Developer Tools trong Trình duyệt bảo vệ | Trong Tab Browser |
| **`Win + Ctrl + Mũi tên Trái/Phải`** | Chuyển đổi giữa Desktop 1 (stream) và Desktop 2 (bảo vệ) *(Có thể khóa qua Khiên Desktop ảo)* | Windows OS |
| **`Win + Ctrl + D` / `Win + Ctrl + F4` / `Win + Tab`** | Tạo/đóng/mở desktop ảo của Windows *(Có thể khóa qua Khiên Desktop ảo)* | Windows OS |

---

## 7. Cơ chế an toàn & Cứu hộ khẩn cấp

Nhằm đảm bảo người dùng không bao giờ bị khóa ngoài hệ thống trong trường hợp máy tính bị tắt đột ngột hoặc xảy ra sự cố ngoại cảnh:

1. **Công cụ cứu hộ khẩn cấp (`Emergency_Restore_Tools.bat`)**:
   - Được đặt sẵn trong thư mục chạy của cả hai bản build.
   - Khi chạy tệp này với quyền Administrator, script sẽ ngay lập tức:
     - Mở khóa Task Manager, Registry Editor, Command Prompt và Group Policy Editor.
     - Khôi phục con trỏ chuột và thiết lập proxy hệ thống về trạng thái mặc định.
     - Dọn sạch các cờ hạn chế tạm thời của Windows.
2. **Nút bấm Cứu hộ trên giao diện**:
   - Trong tab *Riêng tư & Bảo mật*, nút `⚡ Cứu hộ khẩn cấp` cho phép mở khóa toàn bộ công cụ hệ thống chỉ với một cú nhấp chuột.
3. **Tự động hoàn nguyên khi thoát**:
   - Khi bạn đóng ứng dụng bình thường, Driver Booster tự động đưa tất cả các cửa sổ trở lại màn hình chính, gỡ bỏ chính sách khóa công cụ hệ thống và khôi phục cài đặt chuột.

---

## 8. Cấu trúc thư mục dữ liệu & Cấu hình

Toàn bộ cấu hình và dữ liệu của người dùng được phân tách rõ ràng và bảo toàn vĩnh viễn:

```text
%APPDATA%\StreamGuard\
├── config.json                  # Cấu hình chính (ngôn ngữ, phím tắt, bảo vệ, rèm che, khiên chuột)
├── config.recovery.json         # Nhật ký trạng thái khôi phục cửa sổ
├── config.captions.json         # Tùy chọn hiển thị phụ đề trực tiếp
└── Captions\
    └── transcripts\             # Tệp lưu bản ghi phụ đề thời gian thực (.txt và .srt)

%LOCALAPPDATA%\StreamGuard\
├── BrowserProfile\              # Hồ sơ trình duyệt bảo vệ WebView2 độc lập (Cookies, Cache, Storage)
└── CaptionsRuntime\             # Runtime và mô hình nhận diện giọng nói offline (Whisper)
```

> [!NOTE]
> Khi nâng cấp ứng dụng hoặc cập nhật bản build mới, toàn bộ thư mục dữ liệu trên được giữ nguyên vẹn 100%, không bị mất cấu hình hay lịch sử làm việc của bạn.

---

## 9. Hướng dẫn Build & Đóng gói

### Chuẩn bị môi trường
- Cài đặt **.NET 8 SDK** (phiên bản `8.0.400` trở lên): [Tải .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Cài đặt **Git LFS** và chạy `git lfs pull` sau khi clone để tải đầy đủ `Captions/Runtime.zip` (`230.44 MB`).

### Cách 1: Biên dịch bản Unpacked (`net8.0-windows`)
```powershell
dotnet restore
dotnet build -c Release StreamGuard.csproj
```
Kết quả xuất ra tại: `bin\Release\net8.0-windows\`

### Cách 2: Xuất bản độc lập Single-File (`release`)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "..\release" StreamGuard.csproj
```
Kết quả xuất ra tại: `..\release\Driver Booster.exe` (đi kèm các DLL native và `WebView2Loader.dll`).

---

## 10. Kiểm thử tự động (Automated Test Suite)

Dự án sở hữu bộ kiểm thử tự động toàn diện với **6,012 test checks**, bao phủ:
- Kiểm tra hiển thị giao diện WPF và ràng buộc Data Binding ngoài màn hình.
- Rà soát tính toàn vẹn song ngữ (`vi-VN` và `en-US`), thông số định dạng chuỗi.
- Kiểm tra an toàn tiến trình hệ thống, bộ lọc danh sách trắng/đen.
- Kiểm thử cơ chế bảo vệ lưu lượng AI, phát hiện và xóa proxy loopback, quét chứng chỉ MITM.
- Kiểm thử logic phân biệt bộ cài Antigravity Desktop với IDE/CLI và định dạng tham số Silent Install.
- Kiểm thử toàn diện Antigravity Secure Login Bridge (bắt link, timeout, clipboard retry, process inspection).
- Kiểm thử hệ thống Dịch Phụ đề Thời gian thực, định dạng `TOEIC (A-D)` & Từ điển từ khóa chuyên ngành (`CaptionTranslationService`, `CaptionVocabularyService`, `TranscriptToeicFormatter`, bộ nhớ đệm LRU Cache, bảo toàn thuật ngữ kỹ thuật, ghi file TXT/SRT song ngữ).
- Kiểm thử cơ chế chống chụp cửa sổ (`WDA_EXCLUDEFROMCAPTURE`), tàng hình chuột (`InApp` & `GlobalCursorCloak` phối hợp cùng `CursorShieldService`), chụp màn hình trên Chrome Fullscreen (chặn `Escape` / không cướp focus), chống bật/tắt liên tục switch (`SwitchCooldownService`) và hệ thống Toast thông báo.
- Kiểm thử trung tâm khôi phục, độ tương thích DPI và xử lý ngoại lệ.

Chạy kiểm thử bằng lệnh:
```powershell
dotnet run --project tests/StreamGuard.Localization.Tests.csproj -c Release
```
Kết quả xác nhận: **`PASS: 6012 checks.`**

---

## 11. Bản quyền & Giấy phép

- Mã nguồn và tài liệu thuộc dự án **StreamGuard (Driver Booster)**.
- Chi tiết giấy phép phần mềm xem tại [`LICENSE.txt`](LICENSE.txt).
- Thông báo về các thư viện mã nguồn mở bên thứ ba (Newtonsoft.Json, WebView2, CommunityToolkit.Mvvm...) xem tại [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).
