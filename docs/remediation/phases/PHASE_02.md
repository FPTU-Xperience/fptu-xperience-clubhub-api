# BÁO CÁO HOÀN THÀNH PHASE 02 — REPORT ATTACHMENT SECURITY RECONSTRUCTION

> **Thời điểm hoàn tất:** 2026-09-21  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗ hổng CRITICAL Arbitrary Local File Read (`SEC-F10`):**
   - **Thêm cơ chế kiểm tra Path Containment Canonical:** Bổ sung phương thức `ReportAttachmentPolicy.IsPathUnderRoot(candidatePath, rootPath)` sử dụng `Path.GetFullPath` kết hợp dấu phân cách chuẩn hóa để chặn đứng triệt để các kỹ thuật Directory Traversal (`../`, `..\`) và Prefix Sibling Directory Collision (`/attachments_fake`).
   - **Server tự sinh và kiểm soát đường dẫn lưu trữ:**
     - Trong `ReportFileEndpoints.AddAttachmentMetadata`: Server hoàn toàn không tin tưởng hay lưu trực tiếp `request.StoragePath` do client gửi lên.
     - Server tự giải quyết thư mục gốc `storageRoot`, tạo thư mục báo cáo `storageRoot/{report.Id}`, sinh tên file ngẫu nhiên an toàn bằng timestamp + GUID (`ReportAttachmentPolicy.CreateStoredFileName`), và kiểm tra chặt chẽ tính hợp lệ dưới `storageRoot`.
   - **Kiểm tra an toàn khi Download (`DownloadAttachment` & `DownloadUploadedFile`):**
     - Tại endpoint `GET /api/reports/{id}/attachments/{attachmentId}/download`: Bổ sung kiểm tra `ReportAttachmentPolicy.IsPathUnderRoot(attachment.StoragePath, storageRoot)`. Nếu file trỏ ra ngoài thư mục gốc lưu trữ hợp lệ (kể cả do dữ liệu cũ bị chèn mã độc), server sẽ lập tức từ chối và trả về `400 Bad Request ("Invalid attachment path.")`.
     - Tương tự tại endpoint download file chính của báo cáo, đường dẫn được đối soát nghiêm ngặt với `Uploads:StoragePath`.

2. **Ẩn đường dẫn vật lý khỏi Response DTO (`POT-F05`):**
   - Cập nhật [Mappers.cs](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Extensions/Mappers.cs): Khi ánh xạ `ReportAttachment` sang `ReportAttachmentResponse`, gán `StoragePath = string.Empty` thay vì trả về đường dẫn ổ đĩa vật lý của máy chủ (`C:\...` hoặc `/app/...`), bảo vệ an toàn cấu trúc thư mục nội bộ.
   - Cập nhật [ReportContracts.cs](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Contracts/ReportContracts.cs): Chuyển `StoragePath` trong `AddAttachmentRequest` thành tham số tùy chọn (`string? StoragePath = null`) nhằm đảm bảo tương thích ngược tối đa với client cũ.

3. **Xây dựng bộ kiểm thử hồi quy bảo mật chuyên biệt:**
   - Tạo file [ReportAttachmentSecurityTests.cs](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/tests/Backend.StabilizationTests/ReportAttachmentSecurityTests.cs) gồm 17 ca kiểm thử tự động:
     - Kiểm tra đường dẫn hợp lệ trả về `True`.
     - Kiểm tra Directory Traversal các cấp (`../../appsettings.json`, `../../../../Windows/win.ini`, `../../etc/passwd`) trả về `False`.
     - Kiểm tra va chạm tiền tố thư mục anh em (`/attachments_sibling`) trả về `False`.
     - Kiểm tra đường dẫn tuyệt đối hệ điều hành (`C:\Windows\System32\cmd.exe`, `/etc/shadow`) trả về `False`.
     - Kiểm tra làm sạch tên file độc hại (`..\..\evil/payload\0.pdf`).
     - Kiểm tra DTO response không rò rỉ đường dẫn vật lý.

---

## 2. File thay đổi (Files Changed)

- `src/Services/ReportService/Attachments/ReportAttachmentPolicy.cs`: Bổ sung `IsPathUnderRoot`.
- `src/Services/ReportService/Contracts/ReportContracts.cs`: Sửa `AddAttachmentRequest.StoragePath` thành optional.
- `src/Services/ReportService/Extensions/Mappers.cs`: Ẩn `StoragePath` trong `ReportAttachmentResponse`.
- `src/Services/ReportService/Endpoints/ReportFileEndpoints.cs`: Kiểm soát server path trong `AddAttachmentMetadata`, `DownloadAttachment`, và `DownloadUploadedFile`.
- `tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj`: Tham chiếu `ReportService` (loại trừ native assets để tối ưu build).
- `tests/Backend.StabilizationTests/ReportAttachmentSecurityTests.cs`: Tạo mới bộ test bảo mật.
- `docs/remediation/FINDINGS_LEDGER.md`: Cập nhật `SEC-F10` (FIXED), `POT-F05` (FIXED).
- `docs/remediation/TEST_MATRIX.md`: Ghi nhận test pass cho `SEC-F10` và `POT-F05`.
- `docs/remediation/CHECKPOINT.md`: Cập nhật trạng thái chuyển giao sang Phase 03.

---

## 3. Kết quả kiểm thử thực tế

- **Build Solution:** `dotnet build ClubReportHub.sln -c Release -warnaserror` 👉 **0 Warning(s), 0 Error(s)**.
- **Solution Tests:** `dotnet test ClubReportHub.sln -c Release --no-build` 👉 **67/67 tests PASSED** (39 Stabilization + 28 Admin).
- **Stand-alone Tests:** `dotnet test tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj -c Release --no-build` 👉 **39/39 tests PASSED**.
- **Tổng số test toàn hệ thống:** **106/106 tests PASSED** (+17 test mới).
- **Docker Compose:** `docker compose config --quiet` 👉 **Valid**.

---

## 4. Sẵn sàng cho Phase tiếp theo

**Phase 02 đã hoàn tất xuất sắc.** Hệ thống đã bịt hoàn toàn lỗ hổng CRITICAL đọc file tùy ý và sẵn sàng bước sang:
- **`WAVE 1 — PHASE 03: Redis Stream Retention & Capacity Control`**
  - Mục tiêu: Khắc phục lỗ hổng CRITICAL `REL-F03` (Redis Stream tăng trưởng vô hạn dưới giới hạn 128MB noeviction làm dừng event writes) và `POT-F15`.
