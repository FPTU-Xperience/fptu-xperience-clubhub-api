# Chính sách tính điểm theo đề xuất hệ thống

Chốt ngày 02/11/2026 theo yêu cầu của chủ dự án. Nguồn: `FPTU_Xperience_De_xuat_he_thong.pdf`, 6 trang, SHA256 `5919ead86ec0992fb6cd7b4f9368baca609dbb87e6545a1ccab919348c47e5c0`. Trang 2-4 là căn cứ tính điểm; trang 5 quy định xác minh và sổ cái chỉ ghi thêm.

## Công thức được áp dụng

`P = B(T) * min(R * Q, 8) * (1 + K)`.

| Thành phần | Giá trị |
| --- | --- |
| Công sức T1/T2/T3/T4/T5 | 10 / 25 / 60 / 150 / 400 |
| Tham dự / tham gia tích cực / sản phẩm / tổ chức / dẫn dắt | 1 / 1,6 / 2,2 / 3 / 4 |
| CLB / trường / FPT Education / quốc gia / quốc tế | 1 / 1,3 / 1,6 / 2,2 / 3 |
| Chưa có kết quả / chung kết / giải thưởng / đầu ra được sử dụng | 0 / 0,3 / 0,6 / 1 |

Mỗi đóng góp phân bổ vào tối đa ba trụ cột, tổng trọng số bằng 1. Không có xác minh hợp lệ thì không ghi nhận điểm. Không cộng điểm theo số lần đăng ký, số CLB hay số buổi đơn thuần; nhiệm vụ chỉ tham chiếu đóng góp đã xác minh, không cộng lại đóng góp đó.

`X_j = 100 * (1 - exp(-S_j / tau_j))` với S là tổng phân bổ ròng có tính bút toán đảo. Sáu trục radar: Học tập, Nghiên cứu, Quốc tế, Thể thao & Văn hóa, Cộng đồng, Khởi nghiệp. Trải nghiệm thực tế (+1) nằm ngoài sáu trục.

`D = sum(X_core) / 6`; `p_j = X_j / sum(X_core)`; `J = -sum(p_j * ln(p_j)) / ln(6)`; với mọi trục bằng 0 thì J bằng 0, bỏ các hạng p bằng 0 khi lấy log.

`M = 1 + 0,30 * X_+1 / 100`, tối đa 1,30. `ERI = D * (0,5 + 0,5 * J) * M`, tối đa 130. Không lấy tổng điểm thô làm ERI hoặc bảng xếp hạng mặc định.

Điểm gốc giữ tối đa bốn chữ số thập phân; chỉ làm tròn khi trình bày. Ví dụ 171,6 được bảng tài liệu trình bày là 172; 686,4 là 686. Không ghi ngược số đã làm tròn vào sổ cái.

## Phần tài liệu không ấn định một con số cụ thể

Tài liệu yêu cầu tau riêng từng trụ cột, hiệu chỉnh theo phân phối từng học kỳ; không công bố bộ tau hoặc ngưỡng năm bậc. Bản hiện hành giữ cấu hình đã được phê duyệt của FALL2026: tau của bảy trụ cột đều bằng 1000; các mốc hiện hành 20/50/80, bậc Dẫn dắt cần minh chứng; danh hiệu toàn diện dùng mốc tối thiểu 20 và J >= 0,90. Đây là tham số vận hành hiện tại, không phải con số trích từ PDF.

Khi có phân phối dữ liệu học kỳ thật, tạo phiên bản chính sách mới để hiệu chỉnh. Không sửa snapshot đã phê duyệt hay tính lại lịch sử bằng tham số mới mà không có quy trình điều chỉnh. Các hồ sơ minh họa trong PDF không phải dữ liệu đầu vào để ép điểm sinh viên giống hình vẽ.

## Kiểm chứng

Lõi `ExperienceScoringCalculator` và `ExperienceIndexCalculator` đã thực hiện các công thức trên. Bộ kiểm thử bổ sung đối chiếu đủ chín dòng ví dụ trang 3 và trường hợp một trục đạt 100, năm trục bằng 0 cho ERI 8,3 ở trang 4. Không cần thay thuật toán hoặc sửa bút toán để khớp tài liệu.

Việc chuẩn hóa cách trình bày dữ liệu chỉ sửa trường mô tả, tên hiển thị và tài liệu đính kèm thuộc bộ dữ liệu đã tạo. Giữ riêng manifest xuất xứ, bản sao trước thay đổi, thông tin xác thực và lịch sử tính điểm. Nội dung tự nhiên trên giao diện không biến các danh tính đã tạo thành dữ liệu sinh viên thật.
