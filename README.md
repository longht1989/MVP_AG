# LumiePlayer (Windows Video Preview & Inspection App)

Ứng dụng Windows phục vụ xem trước (preview) và kiểm tra video chất lượng cao, tối ưu thao tác chính xác theo từng frame (frame-by-frame) và so sánh hai video với 2 chế độ: **Side by Side (song song)** và **Overlay (chồng hình)** trên cùng một timeline đồng bộ.

---

## 1. Tính năng nổi bật của LumiePlayer

- **Nhận diện & Thương hiệu**:
  - Tên ứng dụng: **LumiePlayer** (`LumiePlayer.exe`).
  - Biểu tượng ứng dụng: Sử dụng `favicon.png` (và `app.ico` đồng bộ) hiển thị sắc nét trên Taskbar, tiêu đề cửa sổ và File Explorer.
- **Tối ưu hiển thị Viewport linh hoạt**:
  - **Single Mode (1 video)**: Khung hình Video A chiếm trọn **100% full viewport**, không còn hiện tượng chia đôi màn hình khi chỉ xem 1 video.
  - **Compare Mode (2 video)**:
    - **Side by Side (Mặc định)**: Hai video hiển thị cạnh nhau, có thanh chia tách co giãn tỷ lệ linh hoạt.
    - **Overlay (Chồng hình)**: Hai video xếp chồng khớp trực tiếp lên nhau trên cùng một khung hình để soi từng pixel.
      - Thanh trượt **Overlay Opacity (0% - 100%)** với các mốc nhanh: `0% [A]`, `25%`, `50%`, `75%`, `100% [B]`.
      - **Quick A/B Flip (Flicker Check)**: Nhấn giữ phím `Tab` (hoặc nút bấm trên thanh công cụ) để chớp nháy nhanh giữa Video A và B, giúp mắt nhận diện ngay sự khác biệt về màu sắc, artifact và chuyển động.
- **Kéo thả nội bộ tiện lợi (Internal Drag & Drop)**:
  - Cho phép người dùng nhấp giữ chuột và kéo bất kỳ video nào từ danh sách **Playlist** thả trực tiếp vào khung **Video A** hoặc **Video B**.
  - Kéo vào Video A $\rightarrow$ Phát tại Video A và trích xuất sóng âm.
  - Kéo vào Video B $\rightarrow$ Nạp vào Video B và **tự động kích hoạt chế độ so sánh (Compare Mode)**.
  - Hiệu ứng viền sáng (Highlight Glow) thông báo trực quan khi rê file qua khung nhận.
  - Vẫn hỗ trợ kéo thả trực tiếp file từ ngoài Windows Explorer vào ứng dụng.
- **Thanh Audio Waveform đối xứng chuyên nghiệp**:
  - Vẽ dạng sóng âm đối xứng 2 chiều qua trục giữa (Mirrored Peak/RMS) chuẩn phòng dựng chuyên nghiệp (DaVinci Resolve / Premiere).
  - Phân vùng màu sắc trực quan: Vùng đã phát (Played) có màu xanh neon rực rỡ (`#38BDF8`), vùng chưa phát (Unplayed) có màu trầm (`#3F3F46`).
  - Thước đo vạch chia nhịp thời gian (Time Ticks) dọc theo thanh sóng âm.
  - Con trỏ Playhead phát sáng độ tương phản cao kèm đường gióng (guide line) và timecode nổi di chuyển mượt mà theo chuột.
  - Nhấp chuột trực tiếp trên waveform để tua nhanh; phím tắt `W` để ẩn/hiện waveform.
- **Thao tác Frame chính xác tuyệt đối (Frame-by-frame)**:
  - Phím `←` / `→` lùi/tiến từng frame khi tạm dừng (`frame-back-step` / `frame-step`).
  - Hiển thị timecode độ chính xác mili-giây `hh:mm:ss.fff` và số frame hiện tại/tổng frame (`Frame: X / Total`).
  - Hộp thoại **Jump to Timecode / Frame**: Nhấp vào timecode để nhập số frame (ví dụ `#2500`) hoặc timecode (`00:01:25.500`) để nhảy ngay lập tức.
- **Điều phối phát & Timeline chung (Playback Coordinator)**:
  - Tự động đồng bộ Player A và Player B trên cùng timeline.
  - Cho phép đặt **Offset cho Video B** (tăng giảm ±0.1s, ±0.01s hoặc reset) để căn chỉnh độ lệch thời gian giữa 2 video.
  - Định tuyến âm thanh khi so sánh: **Audio A**, **Audio B** hoặc **Mute**.
  - Lưu và nạp **Compare Session** (file A, file B, offset, vị trí phát, điểm lặp A-B) dưới dạng JSON.
- **Lặp đoạn A–B (A-B Loop)**:
  - Đặt điểm bắt đầu `[`, kết thúc `]`, xóa lặp `\`. Vùng lặp được đánh dấu trực quan trên waveform và slider timeline.
- **Media Inspector (Xem Metadata chi tiết)**:
  - Kiểm tra độ phân giải, tỷ lệ khung hình, codec video, FPS thực tế, số lượng frame, bitrate, định dạng âm thanh.

---

## 2. Bảng phím tắt (Keyboard Shortcuts)

| Phím tắt | Thao tác |
|---|---|
| `Space` | Phát / Tạm dừng (Play / Pause) |
| `←` / `→` | Lùi / Tiến 1 frame khi tạm dừng (`frame-back-step` / `frame-step`) |
| `Shift + ←` / `Shift + →` | Tua lùi / tiến 1 giây |
| `Tab` | Giữ để xem Video A, thả ra để trở lại Overlay (trong chế độ Overlay) |
| `[` | Đặt điểm bắt đầu lặp (Loop A) |
| `]` | Đặt điểm kết thúc lặp (Loop B) |
| `\` | Xóa đoạn lặp A–B |
| `W` | Ẩn / hiện thanh Audio Waveform |
| `M` | Bật / tắt tiếng (Mute) |
| `F` | Bật / tắt chế độ toàn màn hình (Fullscreen) |
| `Esc` | Thoát chế độ toàn màn hình |
| `Ctrl + O` | Mở Video A (Video tham chiếu chính) |
| `Ctrl + Shift + O` | Mở Video B (Chế độ so sánh) |

---

## 3. Cách chạy ứng dụng

### Chạy trực tiếp bản đã Publish:
Chạy file thực thi:
```powershell
.\bin\publish\LumiePlayer.exe
```
Hoặc mở sẵn video qua tham số:
```powershell
.\bin\publish\LumiePlayer.exe "test_samples\sample_A_30fps.mp4" "test_samples\sample_B_60fps.mp4"
```

### Chạy mã nguồn qua .NET CLI:
```powershell
cd src\WindowsVideoPreview
dotnet run
```

### Chạy bộ kiểm thử tự động:
```powershell
cd tests\WindowsVideoPreview.Tests
dotnet run
```
