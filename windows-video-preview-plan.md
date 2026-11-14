# Kế hoạch xây dựng Windows Video Preview App

## 1. Mục tiêu và phạm vi

Xây dựng ứng dụng Windows phục vụ preview và kiểm tra video, tập trung vào thao tác chính xác theo frame và so sánh hai video trên cùng timeline.

| Nhóm | Chức năng |
|---|---|
| Phát video | Play/pause, tua, thay đổi tốc độ, âm lượng, mute, fullscreen, lặp đoạn A–B |
| Định dạng | Ưu tiên MP4, MOV, MKV, AVI, WebM; codec H.264, H.265, VP9, AV1 thông qua engine giải mã |
| Frame by frame | Tiến/lùi từng frame khi pause; hiển thị thời gian, số frame nếu xác định được; nhập timecode |
| Compare | Mở video A/B, dùng chung timeline, phát/tạm dừng/tua đồng thời |
| Playlist | Thêm file, kéo thả, sắp xếp, xóa, chuyển bài, lưu và mở lại |
| Audio bar | Ẩn/hiện waveform bên dưới timeline; ghi nhớ lựa chọn |
| Metadata | Độ phân giải, codec, FPS, thời lượng, thông tin audio |

Giả định: “audio bar timeline” là waveform âm thanh chạy dọc timeline. Thanh âm lượng là điều khiển riêng.

## 2. Chế độ compare

### Hiển thị

- **Side by side:** hai video cạnh nhau, triển khai trong MVP.
- **Wipe:** kéo đường phân chia để xem một phần A và một phần B, triển khai sau MVP.
- **Overlay:** chồng hai video với độ trong suốt tùy chỉnh, triển khai sau MVP.

Mỗi khung hiển thị tên file, độ phân giải và timecode. Giữ nguyên tỷ lệ hình, không kéo giãn khi hai nguồn khác kích thước.

### Timeline chung

- Đồng bộ theo thời gian, không ghép trực tiếp theo số thứ tự frame.
- Mặc định hai video bắt đầu tại thời điểm 0.
- Cho phép đặt offset cho B để căn chỉnh các video có phần đầu khác nhau.
- Mặc định chỉ phát trong khoảng thời gian cả hai video có dữ liệu sau khi áp dụng offset.
- Audio cho phép chọn A, B hoặc mute; mặc định nghe A.
- Khi step frame, lấy A làm tham chiếu. Mỗi bước đến frame trước/kế tiếp của A; B hiển thị frame tương ứng với thời điểm đó. Áp dụng cả khi A/B khác FPS.

Khả năng seek và mức lệch khi phát phải được kiểm chứng theo engine, codec và phần cứng; chưa cam kết đồng bộ tuyệt đối trước prototype.

## 3. Giao diện và thao tác

```text
┌─────────────────────────────────────────────────────────────┐
│ Open files    Single / Compare    View    Settings           │
├──────────────┬──────────────────────┬───────────────────────┤
│ Playlist     │ Video A              │ Video B               │
│              │                      │ (chế độ Compare)      │
│              │                      │                       │
├──────────────┴──────────────────────┴───────────────────────┤
│ Timeline chung                           Timecode / Frame   │
│ Waveform audio                           [Ẩn / Hiện]        │
├─────────────────────────────────────────────────────────────┤
│ Prev frame   Play/Pause   Next frame   Speed   Audio A/B     │
└─────────────────────────────────────────────────────────────┘
```

| Phím tắt | Thao tác |
|---|---|
| Space | Play/pause |
| ← / → | Lùi/tiến một frame khi pause |
| Shift + ← / → | Tua lùi/tiến một giây |
| F | Fullscreen |
| Ctrl + O | Mở video |
| Ctrl + Shift + O | Mở video B |
| W | Ẩn/hiện waveform |

## 4. Công nghệ và kiến trúc đề xuất

Sử dụng **C# + WPF trên .NET LTS**, **libmpv** để phát video, **FFmpeg/ffprobe** để đọc metadata và tạo waveform.

| Thành phần | Vai trò |
|---|---|
| WPF + MVVM | Giao diện Windows, binding trạng thái, phím tắt, playlist |
| libmpv | Giải mã, phát video, tăng tốc phần cứng, seek và step frame |
| Playback Coordinator | Timeline chung, offset, trạng thái A/B, hiệu chỉnh độ lệch |
| FFmpeg / ffprobe | Metadata và trích xuất dữ liệu âm thanh |
| Waveform Service | Tạo waveform nền, cache và hủy tác vụ |
| JSON | Lưu playlist, phiên compare và thiết lập |

Lựa chọn này giúp phát triển giao diện desktop nhanh và hỗ trợ nhiều định dạng mà không cần tự xây bộ giải mã.

Cần thử nghiệm sớm việc nhúng hai vùng render vào WPF, step lùi và đồng bộ seek. Frame lùi thường cần giải mã lại từ keyframe trước đó nên có thể chậm hơn frame tiến.

Trước khi phân phối, kiểm tra giấy phép của bản build libmpv/FFmpeg và các thành phần đi kèm.

## 5. Các giai đoạn triển khai

### Giai đoạn 1 — Prototype kỹ thuật

- Nhúng libmpv, mở và phát một video.
- Chạy đồng thời hai video.
- Thử tiến/lùi từng frame.
- Thử seek chung, đo độ lệch A/B.
- Kiểm tra video khác FPS, FPS biến đổi và video 4K.
- Xác nhận việc tạo waveform không làm treo giao diện.

**Đầu ra:** prototype, kết quả đo và giới hạn hiệu năng/độ chính xác có thể hỗ trợ.

### Giai đoạn 2 — Trình phát cơ bản

- Hoàn thiện giao diện single video.
- Playback, timeline, tốc độ, âm lượng, fullscreen, phím tắt.
- Playlist kéo thả, sắp xếp, lưu và mở lại.
- Metadata và xử lý file lỗi hoặc bị di chuyển.

### Giai đoạn 3 — Frame và compare

- Frame step và nhập timecode.
- Compare side by side.
- Timeline chung, offset B, chọn nguồn audio.
- Xử lý seek liên tiếp, buffering và kết thúc video.
- Lưu phiên compare: đường dẫn hai file, offset, vị trí xem.

### Giai đoạn 4 — Waveform và hoàn thiện MVP

- Tạo waveform bất đồng bộ, có cache.
- Bật/tắt waveform, ghi nhớ thiết lập.
- Tối ưu resize cửa sổ, DPI và bộ nhớ.
- Đóng gói bản cài đặt Windows, kiểm thử trên máy sạch.

### Giai đoạn 5 — Nâng cấp sau MVP

- Wipe và overlay.
- Zoom/pan đồng bộ hai khung.
- Marker, ghi chú, xuất ảnh frame.
- Difference view hỗ trợ tìm khác biệt hình ảnh.

## 6. Tiêu chí nghiệm thu

- File mẫu thuộc định dạng/codec đã công bố đều phát được; file không hỗ trợ có thông báo rõ ràng.
- Frame tiến/lùi đúng thứ tự trên bộ video kiểm thử, bao gồm FPS biến đổi.
- Khi pause hoặc seek trong compare, hai hình tương ứng đúng thời điểm chung và offset.
- Đo sai lệch timestamp khi phát compare trên cấu hình mục tiêu; ngưỡng nghiệm thu được chốt từ prototype.
- Kéo timeline liên tục không treo hoặc hiển thị kết quả của lần seek cũ.
- Tạo waveform không chặn phát video; file không có audio vẫn hoạt động bình thường.
- Playlist và thiết lập được khôi phục sau khi đóng/mở ứng dụng.

## 7. Phạm vi MVP đề xuất

1. Player cơ bản cho các định dạng phổ biến.
2. Tiến/lùi từng frame.
3. Compare side by side với timeline chung và offset.
4. Playlist có lưu/mở lại.
5. Waveform có thể ẩn/hiện.

Ưu tiên xác nhận độ chính xác frame và đồng bộ A/B trước khi đầu tư vào các chế độ compare nâng cao.
