# Tích hợp LiveAudioCaptions vào Driver Booster

Ngày 03/11/2026. Nguồn đầu vào do người dùng chỉ định: `C:\Users\VirusBetaTester\Downloads\LiveAudioCaptions\LiveAudioCaptions`. Thư mục gốc, `window_state.json` và các bản chép lời cũ không bị thay đổi.

## Sử dụng

Mở **Phụ đề trực tiếp / Live Captions** ở thanh bên rồi chọn **Bật phụ đề**. Cửa sổ nổi hiển thị giọng nói tiếng Anh nhận dạng từ đầu ra loa/tai nghe mặc định của Windows. Đây là nhận dạng tiếng Anh như ứng dụng gốc; chưa bổ sung dịch tự động hay nhận dạng tiếng Việt. Giao diện điều khiển hỗ trợ cả tiếng Việt và tiếng Anh.

- **Tạm dừng / Tiếp tục**: ngừng nhận âm thanh mới và bỏ dữ liệu/kết quả nhận dạng cũ đang chờ. Các file của phiên được giữ mở để tiếp tục ghi.
- **Dừng phụ đề**: kết thúc phiên, ẩn cửa sổ và đóng file. Bật lại tạo cặp TXT/SRT mới.
- **Xem bản chép lời**: mở phần đọc ngay trong tab Phụ đề trực tiếp, thay cho nút mở thư mục. Chọn trong 200 phiên gần nhất và đổi giữa TXT/SRT; văn bản chỉ đọc, có thể chọn để sao chép hoặc dùng nút sao chép nội dung đang xem.
- **Theo dõi phiên đang ghi**: chọn phiên hiện tại và cập nhật khoảng mỗi giây khi phần xem đang mở trong tab phụ đề. Chọn thủ công một phiên cũ sẽ tắt theo dõi để không bị chuyển khỏi nội dung đang đọc. Có thể bật lại bất cứ lúc nào. Đóng phần xem không dừng nhận dạng.
- Bỏ chọn **Theo dõi phiên đang ghi** sẽ giữ nguyên nội dung đang xem để đọc/chọn văn bản; dùng **Làm mới** để đọc thêm. Nếu nội dung chỉ được ghi nối, Làm mới giữ lại vùng chọn hiện tại. Việc nhận dạng và ghi TXT/SRT vẫn tiếp tục độc lập.
- File lớn được xem tối đa 256 KiB cuối, có nhãn thông báo; file đầy đủ vẫn giữ nguyên. Việc đọc chạy ngoài luồng giao diện và cho phép file tiếp tục được ghi. File bị khóa, bị xóa hoặc thiếu định dạng đã chọn sẽ báo lỗi riêng trong phần xem; dùng Làm mới để thử lại. Trình đọc không thay đổi TXT/SRT, cấu hình hoặc trạng thái bảo vệ.
- **Hiện / ẩn phụ đề nổi** hoặc nút **—** trên cửa sổ: chỉ đổi khả năng nhìn thấy cửa sổ, không dừng nhận dạng.
- **×** trên thanh công cụ phụ đề: dừng phụ đề; Driver Booster tiếp tục chạy.
- Kéo phần nội dung để di chuyển, **A− / A+** đổi cỡ chữ từ 12–36. Có thể chọn chỉ hiển thị đoạn mới nhất hoặc giữ thêm đoạn trước.
- **Cho phép nhấp chuột xuyên** làm cửa sổ không nhận nhấp chuột. Bỏ chọn tại màn hình phụ đề của Driver Booster để mở khóa. Không cần phím tắt toàn cục.
- **Đặt lại vị trí phụ đề** giúp đưa cửa sổ về khu vực làm việc chính nếu thay đổi màn hình hoặc vị trí không thuận tiện.

Loại trừ ghi hình mặc định bật và chỉ áp dụng lên cửa sổ phụ đề của Driver Booster. Trạng thái xác minh riêng hiển thị tại màn hình điều khiển. Windows/ứng dụng quay có thể giới hạn tác dụng; không bảo đảm chống mọi cách ghi hình. Màn che của chức năng bảo vệ vẫn có vòng đời riêng.

Nút đóng cửa sổ chính của Driver Booster tiếp tục có hành vi ẩn về khay như trước. Thoát hẳn bằng **Ctrl+Shift+Alt+X** sẽ dừng tiến trình phụ đề. Việc đổi tab hoặc ẩn cửa sổ chính không tự dừng phụ đề.

## Tách biệt chức năng và dữ liệu

Điều khiển và cửa sổ phụ đề dùng WPF trong Driver Booster, không chạy giao diện PyQt, vòng lặp ứng dụng Qt hoặc phím tắt Ctrl+Shift+L/H/S của bản cũ. Nhận dạng/DSP chạy trong một tiến trình Python riêng do Driver Booster sở hữu. Giao tiếp bằng JSON qua stdin/stdout riêng; không mở cổng mạng.

Không tự chạy nhận dạng khi khởi động ứng dụng hoặc khi bắt đầu bảo vệ. Bật/dừng/tạm dừng phụ đề không thay đổi danh sách ứng dụng, cấu hình thiết bị, phím tắt, nhật ký khôi phục hay trạng thái bảo vệ. Tiến trình nội bộ được loại khỏi danh sách/thao tác quản lý tiến trình để không bị bảo vệ, di chuyển hoặc đóng nhầm theo quy tắc của một ứng dụng Python khác.

WASAPI chỉ dùng loopback của đầu ra âm thanh mặc định, downmix các kênh thành mono. Không tìm được đúng thiết bị thì báo lỗi; không chuyển sang micrô vật lý hoặc sang một đầu ra khác. Nếu thiết bị bị ngắt/thay đổi, có thể cần dừng rồi bật lại. Không thay đổi quyền micrô, âm lượng hay thiết bị mặc định của Windows.

Giữ DSP gốc: lọc dải, noise gate, tăng âm lượng số, chia đoạn và chồng đoạn. Hàng đợi âm thanh giới hạn 100 khối × 50 ms; nếu máy xử lý không kịp, loại bớt khối cũ để tránh tích lũy RAM/độ trễ không giới hạn. Điều này có thể bỏ lỡ lời nói khi máy quá tải. Bộ nhận dạng dùng CPU int8, tối đa 4 luồng; không tự nạp CUDA hoặc dò GPU.

Khi dừng, gửi lệnh kết thúc, chờ tối đa khoảng ba giây rồi kết thúc tiến trình phụ bị kẹt. Windows Job Object có `KILL_ON_JOB_CLOSE` để dọn tiến trình phụ khi Driver Booster bị crash. Chỉ tác động tiến trình con thuộc phiên này, không dừng ứng dụng Python hoặc LiveAudioCaptions khác.

## Runtime và file

Runtime Python, thư viện cùng mô hình `base.en` được nhúng trong bản build. Lần đầu bật phụ đề, giải nén vào `%LOCALAPPDATA%\StreamGuard\CaptionsRuntime\<hash>`; các lần sau dùng lại phiên bản đó. Runtime của các bản cập nhật có thể nằm ở thư mục hash khác để tránh thay file đang dùng. Bản chạy lớn hơn trước do có thêm dữ liệu AI/runtime; không cần tải mô hình trong lúc sử dụng.

Thông tin phiên bản chính xác được lưu tại `Captions/runtime-packages.json` và `Captions/requirements-lock.txt`. Model `Systran/faster-whisper-base.en` được lấy tại revision `3d3d5dee26484f91867d81cb899cfcf72b96be6c`. Mã Python tích hợp ở `Captions/Backend`; không import mã từ thư mục LiveAudioCaptions cũ hay Python hệ thống. Dùng Python isolated mode và đường dẫn model cục bộ, tắt mạng Hugging Face và telemetry của Hub.

- Tùy chọn: `%APPDATA%\StreamGuard\config.captions.json`, tách khỏi `config.json` và `config.recovery.json`.
- Kết quả: `%APPDATA%\StreamGuard\Captions\transcripts\transcript_<timestamp>_<id>.txt` và `.srt`.
- File có tên riêng cho mỗi phiên; UTF-8, flush từng đoạn, không ghi nối vào bản chép lời cũ. Transcript chứa nội dung nhận dạng và không được gửi ra ngoài.
- Lưu cỡ chữ, chế độ hiển thị, yêu cầu loại trừ ghi hình và vị trí cửa sổ. Nhấp xuyên mặc định tắt mỗi lần mở để tránh khóa thao tác ngoài ý muốn.
- Quyền và license của các thành phần được giữ trong runtime và `THIRD-PARTY-NOTICES.txt`; tích hợp không đổi chủ sở hữu của thư viện/mô hình.

## Các lỗi đã xử lý trong quá trình tích hợp

- Loại bỏ tự chuyển sang micrô và tự chọn đầu ra loopback không khớp.
- Tránh yêu cầu WASAPI thu một kênh gây dữ liệu sai; đọc các kênh rồi downmix.
- Giới hạn hàng đợi và bỏ kết quả nhận dạng cũ khi tạm dừng.
- Chặn nhiều lần bật đồng thời, xử lý dừng trước khi khởi tạo xong và dọn tiến trình khi chủ bị crash.
- Đọc stdout/stderr song song, giới hạn thông báo và log đuôi để tránh đầy pipe hoặc tích lũy bộ nhớ; lỗi giao thức không được hiện như phụ đề.
- Khởi tạo native NumPy trước luồng đọc stdin để tránh treo lúc nạp trên Windows; hạn chế luồng BLAS.
- Cố định PyAV 16.0.1 tương thích với faster-whisper 1.2.1; bản PyAV 19 thử nghiệm không nhận tham số API mà bộ nhận dạng gọi.
- Bổ sung vocabulary của model, tắt MKL packed GEMM để giảm bộ nhớ khởi động; lỗi nhận dạng được giữ trong mô-đun phụ đề.
- TXT/SRT có định danh phiên riêng; sửa làm tròn mili giây SRT qua mốc giây/phút và loại ký tự xuống dòng khỏi nội dung một mục SRT.

## Phạm vi kiểm tra

Kết quả bản cuối: **2.766 kiểm tra C#**, **23 kiểm tra Python**, **78 kiểm tra Chrome** đều qua. Build Release không có cảnh báo hoặc lỗi; cảnh báo được xem là lỗi trong bước kiểm tra. Đây là số kiểm tra/assertion, không phải tỷ lệ bao phủ toàn bộ ứng dụng.

Bộ C# bao gồm kiểm tra vòng đời của tiến trình riêng, thông báo Unicode, pause/resume, restart, lỗi khởi tạo, lỗi giao thức/đoạn phụ đề, pipe stderr lớn, tiến trình không phản hồi, dừng trong lúc chuẩn bị runtime và Job Object khi tiến trình chủ thực sự bị kết thúc cưỡng bức. Kiểm tra WPF dựng giao diện ngoài màn hình ở 860×600 và 640×460, đổi ngôn ngữ, điều khiển cửa sổ nổi và kiểm tra trạng thái phụ đề/bảo vệ độc lập.

Bộ Python có các kiểm tra DSP cũ và ca mới cho thiết bị thiếu/sai, không dùng micrô, downmix, giới hạn backlog, lỗi thiết bị, dừng trước start, xóa backlog và bỏ kết quả đang nhận dạng khi pause. Ca đầu cuối dùng WAV tiếng Anh tạo từ SAPI, runtime/mô hình đóng gói thật và xuất TXT/SRT thật, không thu nội dung âm thanh của người dùng. Việc tìm thiết bị loopback được thử bằng thao tác chỉ đọc trên máy này.

Không có cam kết nhận dạng đúng mọi giọng nói. Chưa xác nhận live loopback với toàn bộ driver, Bluetooth, HDMI, Remote Desktop, DRM, mọi DPI/màn hình hoặc khi máy thiếu RAM. Thiết bị đổi giữa phiên, âm thanh bị Windows chặn và máy quá tải có thể cần khởi động lại phiên hoặc gây mất đoạn. Bộ kiểm tra không mở sự kiện Loaded của cửa sổ chính, không áp dụng quyền camera/micrô/firewall/hosts thật và không đóng ứng dụng của người dùng.

Đoạn vẫn đang nhận dạng khi bấm dừng/thoát hoặc khi tiến trình bị kết thúc có thể chưa được ghi; các đoạn đã xuất được flush theo từng mục. Timestamp là thời gian tương đối ước tính của phiên, không phải đồng bộ chính xác với một video nguồn.

## Build và cập nhật

Mã và runtime phụ đề được nhúng vào cùng assembly, đi theo cả bản self-contained single-file x64 ở `C:\Users\VirusBetaTester\Downloads\PalStatus\release` lẫn bản đầy đủ EXE/DLL/thư viện ở `C:\Users\VirusBetaTester\Downloads\PalStatus\StreamGuard\bin\Release\net8.0-windows`. Các bản `win-x64` và `win-x64\publish` cũng được đối chiếu khi cập nhật. Không chỉ chép EXE của bản framework-dependent.

Runtime/mô hình khiến bản single-file có kích thước khoảng 386 MiB. Đổi lại, không phải cài Python, PyQt, CUDA hoặc tải mô hình khi bật tính năng. Thay backend Python phải build lại ứng dụng; `Captions/Runtime.zip` dành cho runtime/model, còn các file backend là resource riêng. `tests/captions/prepare_assets.py` tạo archive từ staging có phiên bản trong lock file và giữ các notice/license.

Không ghi đè DLL đang chạy. Kết quả đối chiếu SHA-256 của bản cài được lưu riêng trong báo cáo triển khai; chỉ coi cập nhật hoàn tất khi cả hai vị trí bắt buộc đều khớp. Cấu hình người dùng được đối chiếu trước/sau, cache build và Python phát sinh trong cây nguồn được chuyển vào Thùng rác sau kiểm tra.

Tài liệu kỹ thuật đối chiếu: [faster-whisper](https://github.com/SYSTRAN/faster-whisper), [Python embedded distribution](https://docs.python.org/3.12/using/windows.html#the-embeddable-package), [SoundCard](https://github.com/bastibe/SoundCard), [CTranslate2 environment variables](https://opennmt.net/CTranslate2/environment_variables.html).
