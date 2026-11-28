# Driver Booster (StreamGuard)

> **Giải pháp bảo vệ quyền riêng tư màn hình, chống giám sát từ xa, bảo vệ lưu lượng AI & môi trường làm việc bảo mật toàn diện trên Windows**

Ứng dụng hiện hiển thị tên thương mại chính thức là **Driver Booster**; tệp thực thi chính là **`Driver Booster.exe`** (mã nguồn, namespace và cấu hình kế thừa từ nền tảng cốt lõi **`StreamGuard`**). Ứng dụng được thiết kế chuyên biệt để ngăn chặn toàn diện các hành vi quay lén, chụp trộm màn hình, theo dõi thao tác người dùng, can thiệp tiến trình, nghe lén lưu lượng AI và giám sát từ xa bởi các phần mềm bên ngoài (như RAIA, OBS, Discord, Zoom, Teams, TeamViewer, AnyDesk, Wireshark, Fiddler, Burp Suite...).

Logo chiếc khiên tím đặc trưng được nhúng trực tiếp vào tệp `.exe` và thanh tiêu đề ứng dụng, hỗ trợ hiển thị sắc nét trên Windows và Task Manager từ 16px đến 256px (`Assets/AppIcon.ico` và vector gốc `Assets/AppIcon.svg`).

---

## Mục lục

1. [Yêu cầu hệ thống & Độ tương thích](#1-yêu-cầu-hệ-thống--độ-tương-thích)
2. [Hai vị trí chạy chính thức](#2-hai-vị-trí-chạy-chính-thức)
3. [Giao diện, Thanh bên (Sidebar), Light/Dark Mode & System Tray](#3-giao-diện-thanh-bên-sidebar-lightdark-mode--system-tray)
4. [Đa ngôn ngữ (Language Support)](#4-đa-ngôn-ngữ-language-support)
5. [Chi tiết 8 Phân hệ chức năng chính](#5-chi-tiết-8-phân-hệ-chức-năng-chính)
   - [5.1 Bảng bảo vệ (Shield Dashboard)](#51-bảng-bảo-vệ-shield-dashboard)
   - [5.2 Ứng dụng khả dụng (Running Explorer)](#52-ứng-dụng-khả-dụng-running-explorer)
   - [5.3 Mối đe dọa (Threat Shield & Dual-Engine Firewall)](#53-mối-đe-dọa-threat-shield--dual-engine-firewall)
   - [5.4 Riêng tư & Bảo mật (Privacy & Security)](#54-riêng-tư--bảo-mật-privacy--security)
     - [Bảo vệ lưu lượng AI & Chống soi gói tin (AI Traffic Shield & Proxy Hardening)](#bảo-vệ-lưu-lượng-ai--chống-soi-gói-tin-ai-traffic-shield--proxy-hardening)
     - [Tích hợp bộ cài đặt Antigravity Desktop Offline](#tích-hợp-bộ-cài-đặt-antigravity-desktop-offline)
     - [Cầu nối đăng nhập Antigravity tự động & bảo mật (Antigravity Secure Login Bridge)](#cầu-nối-đăng-nhập-antigravity-tự-động--bảo-mật-antigravity-secure-login-bridge)
     - [Khóa công cụ quản trị hệ thống (System Tools Lock)](#khóa-công-cụ-quản-trị-hệ-thống-system-tools-lock)
     - [Khiên con trỏ chuột & Tàng hình chuột (Cursor Shield & Cursor Cloak)](#khiên-con-trỏ-chuột--tàng-hình-chuột-cursor-shield--cursor-cloak)
     - [Mở khóa sao chép & dán trang web (Copy & Paste Unlocker)](#mở-khóa-sao-chép--dán-trang-web-copy--paste-unlocker)
     - [Chụp màn hình bảo mật & Trình chỉnh sửa ảnh tích hợp (Secure Snipping Tool & Editor)](#chụp-màn-hình-bảo-mật--trình-chỉnh-sửa-ảnh-tích-hợp-secure-snipping-tool--editor)
     - [An toàn thao tác & Vùng nguy hiểm (Passive Focus, Switch Cooldown & Confirmation)](#an-toàn-thao-tác--vùng-nguy-hiểm-passive-focus-switch-cooldown--confirmation)
     - [Khiên khóa phím tắt Desktop ảo (Virtual Desktop Hotkeys Shield)](#khiên-khóa-phím-tắt-desktop-ảo-virtual-desktop-hotkeys-shield)
   - [5.5 Trung tâm khôi phục (Recovery Center)](#55-trung-tâm-khôi-phục-recovery-center)
   - [5.6 Phụ đề trực tiếp & Dịch thời gian thực (Live Captions & Real-time Translation)](#56-phụ-đề-trực-tiếp--dịch-thời-gian-thực-live-captions--real-time-translation)
   - [5.7 Trình duyệt bảo vệ (Protected Chromium Browser - Local Sandbox)](#57-trình-duyệt-bảo-vệ-protected-chromium-browser---local-sandbox)
   - [5.8 Chẩn đoán Hệ thống & Ma trận Đánh giá Tính năng Cốt lõi (Diagnostics)](#58-chẩn-đoán-hệ-thống--ma-trận-đánh-giá-tính-năng-cốt-lõi-diagnostics)
   - [5.9 Cài đặt Hệ thống, Giao diện, Khay hệ thống & Sao lưu (Settings)](#59-cài-đặt-hệ-thống-giao-diện-khay-hệ-thống--sao-lưu-settings)
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
  - Khuyến nghị chạy với quyền Administrator (`Run as administrator`) để kích hoạt đầy đủ Tường lửa dự phòng kép (`Dual-Engine Firewall`), chỉnh sửa hosts cách ly AI, triệt tiêu WinHTTP Proxy và khóa cổng phần cứng Camera/Microphone.

---

## 2. Hai vị trí chạy chính thức

Theo quy chuẩn cập nhật bắt buộc tại [`AGENTS.md`](AGENTS.md), mỗi khi build hoặc nâng cấp chức năng, ứng dụng luôn được cập nhật đồng bộ tại **cả hai vị trí**:

1. **Bản Publish Độc Lập (Single-File Standalone)**:
   - Thư mục: [`C:\Users\VirusBetaTester\Downloads\PalStatus\release`](file:///c:/Users/VirusBetaTester/Downloads/PalStatus/release)
   - Tệp chạy: `Driver Booster.exe` (~407 MB). Đã đóng gói toàn bộ .NET runtime, WPF, thư viện native, font và tài nguyên trong một tệp duy nhất, có thể chạy ngay trên máy mới mà không cần cài thêm .NET.
   - Thư mục kèm theo: `installers\AntigravitySetup.exe` phục vụ cài đặt offline và `WebView2Loader.dll`.
2. **Bản Build Đầy Đủ Thư Viện (Unpacked Assembly)**:
   - Thư mục: [`C:\Users\VirusBetaTester\Downloads\PalStatus\StreamGuard\bin\Release\net8.0-windows`](file:///c:/Users/VirusBetaTester/Downloads/PalStatus/StreamGuard/bin/Release/net8.0-windows)
   - Tệp chạy: `Driver Booster.exe` và `Driver Booster.dll`, đi kèm các tệp `.deps.json`, `.runtimeconfig.json`, `WebView2Loader.dll` và runtimes nền tảng.

> [!IMPORTANT]
> Quy trình cập nhật mặc định giữ hai bản chạy đồng bộ trực tiếp tại đĩa (chỉ đóng gói `.zip` khi người dùng yêu cầu). Bản chạy mới luôn được kiểm tra phù hợp sau khi dọn cache biên dịch (`obj`, `tests\obj`, `tests\bin`).

---

## 3. Giao diện, Thanh bên (Sidebar), Light/Dark Mode & System Tray

- **Light Mode & Dark Mode với hiệu ứng chuyển đổi GPU mượt mà**:
  - Hỗ trợ đầy đủ hai bộ giao diện tương phản cao (`Themes/DarkTheme.xaml` và `Themes/LightTheme.xaml`), chuyển đổi tức thì qua nút **Mặt trời / Mặt trăng (`BtnToggleTheme`)** trên thanh tiêu đề hoặc mục chọn trong Cài đặt (`ThemeModeComboBox`).
  - Hiệu ứng chuyển đổi hiện đại ở 60fps: chụp snapshot khung hình cũ theo đúng DPI bằng `RenderTargetBitmap`, chạy cross-fade `CubicEase` (`360ms`) qua lớp phủ không chặn chuột (`ThemeTransitionOverlay`), kèm ánh sáng chuyển sắc (`ThemeTransitionSheen`) và hiệu ứng xoay/nảy (`BackEase`, `380ms`) trên biểu tượng toggle; đồng thời cập nhật tiêu đề Windows DWM (`DWMWA_USE_IMMERSIVE_DARK_MODE`) và giao diện `PreferredColorScheme` của WebView2.
- **Biểu tượng System Tray linh hoạt (Bật/Tắt khi cần thiết)**:
  - Mặc định tắt (`EnableSystemTrayIcon = false`) để tối ưu tính kín đáo khi chia sẻ màn hình.
  - Khi cần thiết, người dùng có thể bật/tắt ngay trong **1 cú nhấp** qua nút `BtnToggleTray` (kèm chấm báo trạng thái) trên thanh tiêu đề hoặc công tắc trong Cài đặt. Icon dùng biểu tượng chính `Assets/AppIcon.ico`, hỗ trợ double-click để ẩn/hiện và menu chuột phải song ngữ gồm mục `Ẩn biểu tượng khay` (`Hide tray icon`).
- **Hệ thống thông báo Toast trong ứng dụng (`ToastCard` & `ToastNotificationService`)**:
  - Thông báo trượt nổi gọn gàng (tối đa 3 thẻ đồng thời), gộp nhóm thông báo lặp (`x2`, `x3`), tăng tốc thu gọn khi có nhiều sự kiện (Notification Storm), hỗ trợ kéo chuột sang ngang để đóng, tạm dừng đếm ngược khi rê chuột và chuyển tiếp cuộn chuột xuống nội dung bên dưới.
- **Bộ biểu tượng vector & Thanh bên co giãn thông minh**:
  - Kéo mép phải thanh bên từ **200 px đến 300 px**, kéo dưới 160 px để thu gọn dạng mini **60 px** (chỉ hiện icon), tự thu gọn/mở rộng khi đổi kích thước cửa sổ, nút Hamburger (☰), và điều khiển đầy đủ bằng bàn phím (`Tab`, `Trái/Phải`, `Home`, `End`, `Esc`).

---

## 4. Đa ngôn ngữ (Language Support)

- Hỗ trợ song ngữ hoàn chỉnh: **Tiếng Việt (`vi-VN`)** và **English (`en-US`)**.
- **Chuyển đổi tức thì**: Đổi ngôn ngữ ngay qua phím tắt **F4** hoặc trong Cài đặt (`LanguageComboBox`). Toàn bộ giao diện, thanh điều hướng, bảng chẩn đoán, trình chỉnh sửa ảnh, menu System Tray, phụ đề và Toast đều cập nhật ngay lập tức.
- **Độc lập dữ liệu**: Đổi ngôn ngữ không làm gián đoạn ứng dụng đang được bảo vệ hoặc phím tắt đã đăng ký.
- Lưu tự động tại trường `Language` trong `%APPDATA%\StreamGuard\config.json`.

---

## 5. Chi tiết 8 Phân hệ chức năng chính

### 5.1 Bảng bảo vệ (Shield Dashboard)
- **Quản lý danh sách ứng dụng được bảo vệ**: Hiển thị trực quan từng cửa sổ đang được ẩn/bảo vệ, biểu tượng ứng dụng, PID, phím tắt bảo vệ riêng và chế độ hoạt động.
- **Cơ chế ẩn màn hình ảo (Virtual Desktop Cloak)**:
  - Tự động di chuyển ứng dụng được bảo vệ sang một Virtual Desktop độc lập (`IVirtualDesktopManager::MoveWindowToDesktop`).
  - Ứng dụng biến mất hoàn toàn khỏi Desktop 1 nhưng vẫn chạy mượt mà ở Desktop 2; hỗ trợ chuyển qua lại bằng `Win + Ctrl + Trái/Phải` khi cần.
- **Cơ chế dự phòng tự động (Automatic Fallback)**:
  - Nếu môi trường không hỗ trợ Virtual Desktop COM, tự động chuyển sang **WDA In-place Wrapper** (`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`) hoặc Win32 Hide.
- **Tối ưu hiệu năng quét nền**: Dùng `WindowEnumerator.TryGetExecutablePathFast` (`QueryFullProcessImageNameW` qua `PROCESS_QUERY_LIMITED_INFORMATION`) và dùng chung một snapshot cửa sổ mỗi chu kỳ kiểm tra 1 giây trong `ReconcileProtectedWindows`, loại bỏ hoàn toàn tình trạng duyệt danh sách DLL (`Process.MainModule`) gây nghẽn UI thread.
- **Nút Khẩn Cấp (Panic Restore)**: Bấm `Panic: Khôi phục tất cả` hoặc nhấn `Ctrl+Shift+Alt+H` để đưa toàn bộ ứng dụng về Desktop chính ngay tức khắc.

### 5.2 Ứng dụng khả dụng (Running Explorer)
- Quét và liệt kê toàn bộ các cửa sổ và tiến trình ứng dụng đang mở với truy vấn tên thực thi tốc độ cao và cache theo PID.
- **Bộ lọc an toàn tiến trình hệ thống (`ProcessSafetyService`)**: Tự động nhận diện và loại trừ tiến trình lõi Windows (`SystemApps`, `servicing`, `System32`/`SysWOW64`, Session 0, Critical processes) khỏi nguy cơ thao tác nhầm.
- **Thao tác nhanh**: Chọn từ danh sách, bấm `+ Add` hoặc kéo thả trực tiếp tệp `.exe` vào ứng dụng.

### 5.3 Mối đe dọa (Threat Shield & Dual-Engine Firewall)
- **Nhận diện 33+ nhóm công cụ giám sát**: Quét thời gian thực các phần mềm ghi hình, chụp ảnh, họp trực tuyến và điều khiển từ xa (OBS Studio, Discord, Camtasia, Bandicam, Zoom, Microsoft Teams, TeamViewer, AnyDesk, v.v.).
- **Rèm che bảo mật tự động (`Privacy Curtain`)**: Phủ rèm che khi phát hiện rủi ro hoặc kích hoạt thủ công; nhấn `Ctrl+Shift+Alt+C` để gỡ/tạm dừng khẩn cấp.
- **Tường lửa dự phòng kép (`Dual-Engine Firewall`)**: Kết hợp PowerShell và `netsh advfirewall` (tự động fallback khi môi trường khóa PowerShell).
- **Khóa thiết bị phần cứng**: Ngắt quyền truy cập Microphone và Camera cấp hệ thống khi cần.
- **Điều tiết chu kỳ quét (`Adaptive Polling`)**: Tự giãn lên 5 giây khi thu nhỏ xuống nền và rút về 2 giây khi hoạt động hoặc phát hiện rủi ro; bổ sung khóa chống chồng chéo nhịp quét (`Monitor.TryEnter`).

---

### 5.4 Riêng tư & Bảo mật (Privacy & Security)

#### Bảo vệ lưu lượng AI & Chống soi gói tin (AI Traffic Shield & Proxy Hardening)
Phân hệ chuyên dụng bảo vệ toàn vẹn prompts/responses khi tương tác với các AI và AI Agent (Antigravity, Gemini, ChatGPT...):
- **Tự động triệt tiêu WinInet Rogue Proxy**: Phát hiện và xóa cờ Proxy loopback (`127.0.0.1:8888`, `8080`, `localhost`, PAC URL đáng ngờ) trong Registry `Internet Settings`, phát tín hiệu làm mới WinInet ngay lập tức.
- **Kiểm tra WinHTTP Proxy**: Đọc khóa `WinHttpSettings` phát hiện cấu hình proxy hệ thống mức dịch vụ để cảnh báo sớm nếu bị ép chuyển hướng lưu lượng.
- **Kiểm tra & dọn biến môi trường Proxy / TLS Bypass (`EnvProxyAssessment`)**: Giám sát và xử lý các biến môi trường `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NODE_TLS_REJECT_UNAUTHORIZED`, `SSL_CERT_FILE`, `REQUESTS_CA_BUNDLE`, `NODE_EXTRA_CA_CERTS` ngăn chặn hạ cấp xác thực TLS hoặc ép đi qua MITM proxy.
- **Quét chứng chỉ Root CA giả mạo MITM**: Rà soát cả `LocalMachine\Root` và `CurrentUser\Root` phát hiện chứng chỉ gốc do Fiddler, Charles, mitmproxy, Proxyman, Burp Suite... cài lén.
- **Giám sát Network Sniffer**: Phát hiện nhanh Wireshark, Fiddler, HTTP Debugger, Process Hacker/System Informer.
- **Bảo vệ tệp hosts cho AI APIs**: Giám sát và tự động dọn mục chuyển hướng giả mạo trong `hosts` đối với các endpoint AI của Google (`generativelanguage.googleapis.com`, `gemini.google.com`, `aiplatform.googleapis.com`, v.v.).

#### Tích hợp bộ cài đặt Antigravity Desktop Offline
- Kèm sẵn bộ cài `installers\AntigravitySetup.exe` ở cả hai bản build để cài offline 100%.
- Hỗ trợ **Cài đặt trực quan** và **Cài đặt ngầm (Silent Install)** (tương thích Inno Setup lẫn NSIS).
- Phân biệt chính xác **Antigravity Desktop** với Antigravity IDE / CLI (`agy`), hiển thị badge trạng thái động, nút khởi chạy an toàn và công tắc tắt thông báo (`MuteAntigravityNotifications`).

#### Cầu nối đăng nhập Antigravity tự động & bảo mật (Antigravity Secure Login Bridge)
- Bắt tự động luồng Google OAuth từ Antigravity Desktop qua **WMI Process Watcher** (`Win32_ProcessStartTrace`) và **Clipboard Polling** (chống xung đột `CLIPBRD_E_CANT_OPEN`).
- Ngắt trình duyệt ngoài không có chống quay màn hình, chuyển URL hợp lệ vào **Trình duyệt bảo vệ (`Protected Chromium Browser`)**, và bắt redirect `localhost` / `127.0.0.1` sau khi xác thực xong (kèm kiểm tra đã cài đặt và tự hủy chờ sau 90 giây).

#### Khóa công cụ quản trị hệ thống (System Tools Lock)
- Khóa truy cập **Task Manager** (`taskmgr.exe`), **Registry Editor** (`regedit.exe`), **Command Prompt** (`cmd.exe`) và **Group Policy Editor** (`gpedit.msc`); tự động mở khóa khi thoát bình thường hoặc qua nút/script cứu hộ.

#### Khiên con trỏ chuột & Tàng hình chuột (Cursor Shield & Cursor Cloak)
- **Khiên con trỏ chuột (`Cursor Shield`)**: Chuẩn hóa con trỏ hệ thống khi bật bảo vệ.
- **Tàng hình chuột trên ứng dụng (`In-App Cursor Cloak`)**: Ẩn con trỏ thật trong cửa sổ `Driver Booster` và hiển thị con trỏ giả lập cục bộ.
- **Tàng hình chuột toàn màn hình (`Global Cursor Cloak` — `Ctrl + Shift + M`)**: Ẩn con trỏ thật toàn hệ thống khi chia sẻ màn hình và vẽ lại con trỏ giả lập cục bộ trên `GlobalCursorOverlayWindow` (`WDA_EXCLUDEFROMCAPTURE`, tối ưu cache `CompositionTarget` ở 60–144Hz); hỗ trợ thoát khẩn cấp bằng `Ctrl + Shift + Esc`.

#### Mở khóa sao chép & dán trang web (Copy & Paste Unlocker)
- Cung cấp sẵn script (`BrowserScripts/EnableCopyPaste.js`) gồm 3 chế độ sao chép: **Console F12**, **Bookmarklet**, và **Lệnh hoàn nguyên**.

#### Chụp màn hình bảo mật & Trình chỉnh sửa ảnh tích hợp (Secure Snipping Tool & Editor)
- Phím tắt mặc định **`Ctrl + Alt + S`** (tùy chỉnh được trong Cài đặt), hoạt động êm ái trên cả **Chrome Fullscreen** mà không làm mất focus (`WS_EX_NOACTIVATE` + `MA_NOACTIVATE`, `WDA_EXCLUDEFROMCAPTURE`, hook `Escape`).
- **Trình chỉnh sửa ảnh sau khi chụp (`SnippingEditorWindow`)**:
  - **Cắt ảnh theo vùng (Crop)** bằng kéo thả trực tiếp + `Enter` để cắt, `Esc` để hủy chọn.
  - **Xoay ảnh trái/phải 90°** và **Lật ngang / Lật dọc**.
  - **Điều chỉnh kích thước (Resize)** theo pixel nhập tay hoặc nút nhanh **75% / 50%**, có khóa tỷ lệ khung hình (`LockAspect`).
  - **Làm mờ vùng chọn & Làm mờ toàn ảnh** với 2 chế độ **Mosaic (Ô vuông)** và **Gaussian Box Blur** mượt dùng thuật toán **Sliding-Window $O(W \times H)$** tốc độ cao; trang bị cơ chế **chống spam nút Làm mờ toàn ảnh** (giới hạn tối đa **3 lần** trên mỗi ảnh và cooldown `800ms`).
  - **Thu / Phóng & Kính lúp**: Phóng to/thu nhỏ từ **25% đến 500%** bằng nút zoom, `Ctrl + Cuộn chuột` hoặc `Ctrl + +/-/0`, kéo chuột phải để pan ảnh, và bật **Kính lúp 2.5x (`ToggleMagnifier`)** soi chi tiết điểm ảnh.
  - **Lịch sử Undo / Redo** (lưu tối đa 15 bước, tự động `Dispose` bitmap giải phóng RAM), kèm tùy chọn `ScreenshotOpenEditorAfterCapture` và tùy chọn **`AllowSelfScreenCapture`** cho phép tự quay/chụp cửa sổ Driver Booster khi cần demo.

#### An toàn thao tác & Vùng nguy hiểm (Passive Focus, Switch Cooldown & Confirmation)
- **Passive Focus Mode**: Giữ cửa sổ ở chế độ không cướp focus khi thao tác song song.
- **Chống spam công tắc (`SwitchCooldownService`)**: Hạn chế bật/tắt liên tục với độ trễ `1.2 giây` cho mỗi switch kèm Toast cảnh báo.
- **Xác nhận Vùng nguy hiểm**: Hộp thoại xác nhận trước khi chạy *Factory Reset & Wipe Data* hoặc *Complete Clean Uninstall*.

#### Khiên khóa phím tắt Desktop ảo (Virtual Desktop Hotkeys Shield)
- Chặn có chọn lọc các phím Desktop ảo `Win + Ctrl + D`, `Win + Ctrl + F4`, `Win + Ctrl + Trái/Phải` và `Win + Tab` qua `WH_KEYBOARD_LL` (vẫn giữ nguyên `Win + D`, `Alt + Tab`, `Ctrl + Tab`).

---

### 5.5 Trung tâm khôi phục (Recovery Center)
- Lưu nhật ký khôi phục độc lập tại `%APPDATA%\StreamGuard\config.recovery.json`.
- Nếu máy tắt nguồn đột ngột, khi khởi động lại sẽ liệt kê đầy đủ các cửa sổ chưa khôi phục để người dùng bấm *Thử khôi phục lại*, *Khôi phục tất cả* hoặc *Kiểm tra lại* HWND thực tế.

---

### 5.6 Phụ đề trực tiếp & Dịch thời gian thực (Live Captions & Real-time Translation)
- **Speech-to-Text Offline**: Tích hợp Whisper `int8` và Python runtime biệt lập qua WASAPI Loopback; tối ưu bộ nhớ MKL (`MKL_NUM_THREADS=1`, `OMP_NUM_THREADS=2`).
- **Dịch thời gian thực sang Tiếng Việt (<200ms)**:
  - Hỗ trợ chế độ **Song ngữ (Bilingual)** và **Chỉ Tiếng Việt (`VietnameseOnly`)**, phím tắt **`Ctrl + Alt + T`** hoặc nút **`VI`** trên cửa sổ phụ đề nổi, kèm **LRU Cache** (2,000 cụm từ).
- **Bản chép lời Song ngữ, Định dạng `TOEIC (A-D)` & Thao tác nhanh**:
  - Lưu đồng thời `.txt` và `.srt` theo thời gian thực (`AutoFlush = true`) tại `%APPDATA%\StreamGuard\Captions\transcripts\`.
  - Hỗ trợ xem **`TXT | SRT | TOEIC (A-D)`**, tìm kiếm trong bản chép lời, **Sao chép nhanh** (kể cả tệp > 256 KB), **Xoá nhanh**, **Xoá phiên cũ / Xoá từng phiên** vào Thùng rác Windows.
- **Từ điển từ khóa chuyên ngành (`Smart Vocabulary & Jargon Guard`)**:
  - Nút **Lưu từ điển** tích hợp kiểm tra hợp lệ chặt chẽ: không cho phép lưu khi trống, yêu cầu mỗi từ khóa dài 2–64 ký tự (chứa chữ hoặc số), tối đa 100 từ khóa / 2,000 ký tự, không chứa ký tự điều khiển.
  - Chuẩn hóa lỗi nhận diện âm học Whisper (ví dụ *anti gravity* → **Antigravity**, *jiminy* → **Gemini**, *c-sharp* → **C#**, *dot net* → **.NET**, *driver booster* → **Driver Booster**) và bảo toàn thuật ngữ khi dịch.
- **Cửa sổ nổi chống chụp (`CaptionOverlayWindow`)**: Hỗ trợ `WDA_EXCLUDEFROMCAPTURE`, nhấp xuyên (Click-through), chỉnh cỡ chữ (`A− / A+`) và 1 hoặc 2 dòng.

---

### 5.7 Trình duyệt bảo vệ (Protected Chromium Browser - Local Sandbox)
- Sử dụng **Microsoft Edge WebView2** chạy độc lập trong hồ sơ hộp cát riêng `%LOCALAPPDATA%\StreamGuard\BrowserProfile`, đồng bộ `PreferredColorScheme` (Dark/Light) theo giao diện ứng dụng, được bảo vệ bởi `WDA_EXCLUDEFROMCAPTURE`.
- Tích hợp Omnibox thông minh, Back/Forward/Refresh/Home, DevTools (`F12`), công cụ tiêm script mở khóa **Copy/Paste**, **Anti-Blur / F11 Fullscreen Lock**, nút **Đăng nhập Antigravity (Tự động)** và nút mở bằng trình duyệt ngoài.

---

### 5.8 Chẩn đoán Hệ thống & Ma trận Đánh giá Tính năng Cốt lõi (Diagnostics)
Phân hệ **Chẩn đoán (`ViewDiagnostics`)** phân tích toàn diện phần cứng và khả năng tương thích của máy tính:
- **Kiểm tra thông số phần cứng & môi trường**: CPU (tên, số nhân/luồng, AVX, AVX2), RAM khả dụng & tỷ lệ tải, GPU & VRAM, Ổ đĩa trống, Hệ điều hành (Build, 64-bit, quyền Administrator), Thiết bị Audio/Loopback, và phiên bản **WebView2 Runtime**.
- **Ma trận đánh giá tính năng cốt lõi (Tiếng Việt / English đầy đủ)**: Đánh giá mức độ sẵn sàng từng lớp bảo vệ (Anti-Capture WDA, Live Captions AVX2, Virtual Desktop COM, AI Traffic Shield Admin, Protected Browser WebView2) và xếp hạng tổng thể (**Optimal / Good / Warning / Insufficient**) kèm khuyến nghị cụ thể và nút sao chép báo cáo chẩn đoán.

---

### 5.9 Cài đặt Hệ thống, Giao diện, Khay hệ thống & Sao lưu (Settings)
- **Tự kích hoạt khiên khi mở (`AutoStartShield`)**: Đồng bộ hai chiều giữa tab *Bảng bảo vệ* và tab *Cài đặt*.
- **Quản lý Giao diện (`ThemeMode`) & Biểu tượng khay (`EnableSystemTrayIcon`)**: Chọn Chế độ Sáng/Tối và bật/tắt System Tray lưu bền vững.
- **Sao lưu & Khôi phục JSON (`Backup & Restore`)**: Xuất toàn bộ `config.json` (ứng dụng, phím tắt, giao diện, system tray, AI shield, cài đặt phụ đề) ra tệp JSON độc lập để mang sang máy khác và nhập lại an toàn.
- **Vùng nguy hiểm (`Danger Zone`)**: Cung cấp nút *Đặt lại mặc định & Dọn dữ liệu* (Factory Reset) và *Gỡ cài đặt sạch hoàn toàn* (Clean Wipe) có hộp thoại xác nhận trước khi thực thi.

---

## 6. Bảng phím tắt toàn cầu (Global Hotkeys)

| Phím tắt | Chức năng | Phạm vi |
| :--- | :--- | :--- |
| **`Ctrl + Shift + G`** hoặc **`Shift + S + G`** | Hiện hoặc ẩn cửa sổ Driver Booster | Toàn cầu (Global) |
| **`Ctrl + Shift + Alt + H`** | **Panic Restore**: Khôi phục khẩn cấp toàn bộ cửa sổ về Desktop 1 | Toàn cầu (Global) |
| **`Ctrl + Shift + Alt + C`** | Gỡ hoặc tạm dừng Rèm che bảo vệ (`Privacy Curtain`) | Toàn cầu (Global) |
| **`Ctrl + Shift + M`** | Bật hoặc tắt **Tàng hình chuột trên toàn màn hình** (`Global Cursor Cloak`) | Toàn cầu (Global) |
| **`Ctrl + Shift + Esc`** | Khôi phục khẩn cấp con trỏ chuột Windows mặc định | Toàn cầu (Global) |
| **`Ctrl + Alt + S`** (hoặc cấu hình) | Kích hoạt công cụ Chụp ảnh màn hình bảo mật | Toàn cầu (Global) |
| **`Ctrl + Alt + T`** | Bật hoặc tắt Dịch phụ đề thời gian thực sang Tiếng Việt | Toàn cầu (Global) |
| **`F4`** | Mở bộ chọn ngôn ngữ hiển thị (Tiếng Việt / English) | Trong ứng dụng |
| **`F12`** | Mở công cụ Developer Tools trong Trình duyệt bảo vệ | Trong Tab Browser |
| **`Ctrl + Z / Y / S / C`** & **`Ctrl + +/-/0`** | Hoàn tác/Làm lại/Lưu/Sao chép & Thu phóng ảnh trong Snipping Editor | Trong Snipping Editor |
| **`Win + Ctrl + Mũi tên Trái/Phải`** | Chuyển đổi giữa Desktop 1 (stream) và Desktop 2 (bảo vệ) *(Có thể khóa qua Khiên Desktop ảo)* | Windows OS |
| **`Win + Ctrl + D` / `Win + Ctrl + F4` / `Win + Tab`** | Tạo/đóng/mở desktop ảo của Windows *(Có thể khóa qua Khiên Desktop ảo)* | Windows OS |

---

## 7. Cơ chế an toàn & Cứu hộ khẩn cấp

Nhằm đảm bảo người dùng không bao giờ bị khóa ngoài hệ thống trong trường hợp máy tính bị tắt đột ngột hoặc xảy ra sự cố ngoại cảnh:

1. **Công cụ cứu hộ khẩn cấp (`Emergency_Restore_Tools.bat`)**:
   - Được tạo/đặt sẵn cạnh tệp chạy. Khi chạy với quyền Administrator, script lập tức:
     - Mở khóa Task Manager, Registry Editor, Command Prompt và Group Policy Editor.
     - Khôi phục con trỏ chuột, dọn chính sách khóa tạm thời và thiết lập proxy hệ thống về mặc định.
2. **Nút bấm Cứu hộ trên giao diện**:
   - Nút cứu hộ trong tab *Riêng tư & Bảo mật* cho phép mở khóa công cụ hệ thống và khôi phục con trỏ Windows chỉ bằng một lần bấm.
3. **Tự động hoàn nguyên khi thoát & Startup Self-Healing**:
   - Khi khởi động (`App.OnStartup`) hoặc thoát bình thường (`CleanupAndExit`), ứng dụng tự động hoàn nguyên khóa hệ thống/con trỏ còn sót từ phiên trước, hủy đăng ký các hook/timer và giải phóng hoàn toàn icon System Tray.

---

## 8. Cấu trúc thư mục dữ liệu & Cấu hình

Toàn bộ cấu hình và dữ liệu người dùng được tách biệt an toàn ngoài thư mục cài đặt:

```text
%APPDATA%\StreamGuard\
├── config.json                  # Cấu hình chính (Apps, Language, ThemeMode, EnableSystemTrayIcon,
│                                #   SnippingHotkey, Screenshot*, EnableAiTrafficShield,
│                                #   AllowSelfScreenCapture, AutoStartShield, Hotkeys...)
├── config.json.bak              # Bản sao lưu an toàn tự động trước mỗi lần ghi config
├── config.recovery.json         # Nhật ký trạng thái khôi phục cửa sổ độc lập
├── config.captions.json         # Tùy chọn hiển thị phụ đề & từ điển từ khóa
├── crash.log                    # Nhật ký sự cố (nếu có lỗi nghiêm trọng khi khởi chạy/chạy nền)
└── Captions\
    └── transcripts\             # Tệp lưu bản ghi phụ đề thời gian thực (.txt và .srt)

%LOCALAPPDATA%\StreamGuard\
├── BrowserProfile\              # Hồ sơ trình duyệt bảo vệ WebView2 độc lập (Cookies, Cache, Storage)
└── CaptionsRuntime\             # Runtime và mô hình nhận diện giọng nói offline (Whisper)
```

> [!NOTE]
> Khi cập nhật bản build mới tại `release` hoặc `bin\Release\net8.0-windows`, toàn bộ `%APPDATA%\StreamGuard` của người dùng được giữ nguyên vẹn 100%.

---

## 9. Hướng dẫn Build & Đóng gói

### Chuẩn bị môi trường
- Cài đặt **.NET 8 SDK** (phiên bản `8.0.400` trở lên): [Tải .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Cài đặt **Git LFS** và chạy `git lfs pull` sau khi clone để tải đầy đủ `Captions/Runtime.zip` (~230 MB).

### Cách 1: Biên dịch bản Unpacked (`net8.0-windows`)
```powershell
dotnet build StreamGuard.csproj -c Release -m:1
```
Kết quả xuất ra tại: `bin\Release\net8.0-windows\` (`Driver Booster.exe`, `Driver Booster.dll`, `Driver Booster.deps.json`, `Driver Booster.runtimeconfig.json`, `WebView2Loader.dll`).

### Cách 2: Xuất bản độc lập Single-File (`release`)
```powershell
dotnet publish StreamGuard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -m:1 -o "..\release"
```
Kết quả xuất ra tại: `..\release\Driver Booster.exe`.

*(Ghi chú: Tham số `-m:1` giúp MSBuild biên dịch ổn định, tránh quá tải RAM khi đóng gói đồng thời `Runtime.zip` cỡ lớn.)*

---

## 10. Kiểm thử tự động (Automated Test Suite)

Dự án sở hữu bộ kiểm thử tự động toàn diện với **`6,993` checks** (`0 Warning(s), 0 Error(s)`), bao phủ:
- **`ThemeChecks.cs`**: Kiểm tra đầy đủ resource keys ở `DarkTheme.xaml` & `LightTheme.xaml`, độ tương phản WCAG (`>= 10:1` chữ chính, `>= 4.5:1` nhãn phụ/trạng thái), lưu `ThemeMode` & `EnableSystemTrayIcon`, chuyển giao diện + System Tray ngay trên `MainWindow` thật và kiểm tra overlay chuyển cảnh (`ThemeTransitionOverlay`).
- **`SnippingChecks.cs`**: Kiểm tra trình chỉnh sửa ảnh sau khi chụp (Crop, Rotate, Flip, Resize, Mosaic/BoxBlur, giới hạn chống spam Blur tối đa 3 lần + cooldown, Zoom 25%–500%, Undo/Redo, phím tắt Chrome Fullscreen).
- **`OptimizationAuditChecks.cs`**: Kiểm tra truy vấn đường dẫn tiến trình nhanh (`TryGetExecutablePathFast`), snapshot dùng chung `WindowEnumerator`, độ chính xác & hiệu năng thuật toán làm mờ Sliding-Window $O(W \times H)$ ở độ phân giải 1080p bán kính 32, và `ToastCard` freeze brush.
- **`AiTrafficChecks.cs` & `CaptureGuardChecks.cs`**: Bảo vệ lưu lượng AI (WinInet Proxy, WinHTTP Proxy, Environment Proxy/TLS Bypass, MITM Root CA, Hosts), Adaptive Polling, WDA Capture Exclusion.
- **`DiagnosticsChecks.cs`, `BackupChecks.cs`, `CleanWipeChecks.cs`**: Ma trận đánh giá chẩn đoán phần cứng song ngữ, Export/Import cấu hình JSON và dọn dữ liệu an toàn.
- **`CaptionTranslationChecks.cs`, `CaptionVocabularyChecks.cs`, `TranscriptChecks.cs`, `BrowserChecks.cs`, `RecoveryChecks.cs`, `StealthFocusChecks.cs`, `CursorCaptureProtectionChecks.cs`, `ToastNotificationChecks.cs`, `SwitchCooldownChecks.cs`, `AuditChecks.cs`**: Toàn bộ song ngữ `vi-VN`/`en-US`, Whisper/Dịch/TOEIC/Từ điển validate, WebView2 Sandbox, khôi phục cửa sổ, tàng hình chuột, Switch Cooldown và binding WPF không lỗi.

Chạy kiểm thử bằng lệnh:
```powershell
dotnet build tests\StreamGuard.Localization.Tests.csproj -c Release -m:1
.\tests\bin\Release\net8.0-windows\StreamGuard.Localization.Tests.exe
```
Kết quả xác nhận gần nhất: **`PASS: 6993 checks.`**

---

## 11. Bản quyền & Giấy phép

- Mã nguồn và tài liệu thuộc dự án **StreamGuard (Driver Booster)**.
- Chi tiết giấy phép phần mềm xem tại [`LICENSE.txt`](LICENSE.txt).
- Thông báo về các thư viện mã nguồn mở bên thứ ba (Newtonsoft.Json, Microsoft.Web.WebView2, CommunityToolkit.Mvvm, System.Management...) xem tại [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).
