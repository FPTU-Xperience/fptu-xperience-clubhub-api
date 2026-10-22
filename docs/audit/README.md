# Tài liệu tiến độ BE

- [Triển khai và bàn giao contract BE — 22/10/2026](TRIEN_KHAI_CONTRACT_BE_2026-10-22.md): tiến độ sau audit 21/10; route mới, lỗi đã sửa, metadata, migration và phần còn thiếu. Dùng mốc này khi tích hợp các thay đổi trên nhánh `feat/backend-contract-completion`.
- [JSON tiến độ triển khai — 22/10](TRIEN_KHAI_CONTRACT_BE_2026-10-22.json): 6 operation công khai mới, 15 consumer operation còn thiếu route theo actor và kết quả 788 test thực chạy.
- [Đối chiếu BE với contract FE main — 21/10/2026](DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.md): mốc trước triển khai, theo actor/nhóm contract, 16 operation có consumer nhưng chưa có route, các lệch DTO/nghiệp vụ và kiểm thử BE thực chạy.
- [Dữ liệu đối chiếu JSON — 21/10](DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.json): commit/hash nguồn, wrapper và route inventory, findings, scope và kết quả test; nhãn declaration tách riêng nhãn nghiệp vụ.
- [Bảng trạng thái API — 14/10/2026](TRANG_THAI_API_BE_CHO_FE_2026-10-14.md) và [JSON lịch sử](TRANG_THAI_API_BE_CHO_FE_2026-10-14.json): phạm vi cũ 74 operation, không phải tổng phạm vi contract FE main mới.
- [Bàn giao API Sinh viên](HOAN_THIEN_API_SINH_VIEN_2026-10-13.md): DTO, request, legacy, policy, migration và kiểm thử; cập nhật 14/10.
- [Bàn giao API Quản lý CLB](HOAN_THIEN_API_QUAN_LY_CLB_2026-10-13.md).

Các bảng 12/10–14/10 giữ lại làm lịch sử. Contract FE main ngày 19/10 đã chuyển sang [index/lifecycle và implemented](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/README.md); file be-api-status trên nhánh FE cũ không còn là nguồn hiện hành. Audit 21/10 chỉ cập nhật tài liệu BE, không thay code BE/FE. Đọc rõ phạm vi từng nhãn; có route hoặc test cũ đạt không đồng nghĩa đã hoàn thành contract mới hay đã deploy.
