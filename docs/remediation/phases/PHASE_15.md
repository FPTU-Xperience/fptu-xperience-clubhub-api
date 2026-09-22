# BÁO CÁO HOÀN THÀNH PHASE 15 — DOCUMENT PROCESSING & ASYNCHRONOUS JOB HARDENING

> **Thời điểm hoàn tất:** 2026-09-22  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi HIGH: Xử lý tài liệu Office/PDF đồng bộ trên request thread làm cạn kiệt tài nguyên (`SEC-F14`):**
   - **Hiện trạng trước sửa đổi:**
     - Tải file báo cáo (`ReportExtensions.SaveUploadedReportFileAsync`) đọc toàn bộ luồng tệp vào `MemoryStream`, cấp phát mảng byte `memoryStream.ToArray()`, sau đó băm và ghi ra đĩa. Với các file kích thước lớn (lên tới 20MB), việc này gây áp lực bộ nhớ (GC pressure) nghiêm trọng và nguy cơ OOM trong container giới hạn tài nguyên.
     - Tệp đính kèm OpenXML (DOCX, XLSX) là các archive ZIP không được kiểm soát số lượng mục (entry count) hoặc tổng dung lượng sau giải nén, tiềm ẩn lỗ hổng bảo mật nghiêm trọng (Zip-bomb / Denial of Service) và rủi ro zip-slip traversal.
     - Hàm sinh bản xem trước tài liệu (`ReportPreviewGenerator.GeneratePreviewAsync`) đọc toàn bộ byte của file DOCX bằng `File.ReadAllBytesAsync` và parse toàn bộ body XML không giới hạn phần tử vào QuestPDF.
   - **Giải pháp:**
     - **Streaming upload & băm gia tăng:** Tái cấu trúc `SaveUploadedReportFileAsync` truyền trực tiếp từ `IFormFile.OpenReadStream()` vào `FileStream` đĩa với bộ đệm cố định 80KB (`81920`), đồng thời băm SHA-256 gia tăng qua `IncrementalHash.CreateHash(HashAlgorithmName.SHA256)` mà không tạo bất kỳ byte array trung gian nào trong RAM.
     - **Kiểm soát giải nén & chống Zip-bomb (`ValidateArchiveDecompression`):**
       - Giới hạn tối đa 500 mục (`MaxEntries = 500`).
       - Giới hạn tối đa 50 MB tổng dung lượng giải nén (`MaxTotalUncompressedBytes = 50 MB`).
       - Giới hạn tối đa 30 MB cho một mục đơn lẻ (`MaxSingleEntryUncompressedBytes = 30 MB`).
       - Kiểm tra tỉ lệ nén bất thường (`ratio > 50:1` trên các file lớn) để phát hiện zip-bomb.
       - Chống Zip-Slip bằng cách từ chối các đường dẫn chứa `../` hoặc đường dẫn tuyệt đối bắt đầu bằng `/` hay `\`.
       - Tự động xóa file trên đĩa và trả về lỗi `400 Bad Request` rõ ràng khi phát hiện vi phạm.
     - **Streaming DOCX preview & giới hạn phần tử:**
       - Mở trực tiếp `FileStream` chuyển vào `WordprocessingDocument.Open(fileStream, false)` thay vì `ReadAllBytesAsync`.
       - Giới hạn tối đa 200 phần tử xem trước (`MaxPreviewElements = 200`), bổ sung thông báo tóm lược khi tài liệu vượt ngưỡng để bảo vệ bộ nhớ và tốc độ render PDF của QuestPDF.
       - Dọn dẹp tệp preview rác trên đĩa nếu quá trình render bị hủy hoặc phát sinh ngoại lệ.

2. **Khắc phục lỗi MEDIUM: Cấu hình Hangfire worker mặc định và CancellationToken.None nguy hiểm cho container ít RAM (`REL-F08`):**
   - **Hiện trạng trước sửa đổi:**
     - `ReportService/Program.cs` gọi `builder.Services.AddHangfireServer()` không tham số, nhận cấu hình mặc định của Hangfire (`Environment.ProcessorCount * 5`), có thể tạo hàng chục worker song song trên máy chủ nhiều nhân, làm cạn kiệt RAM container được giới hạn ở 250MB.
     - `ExportService/Program.cs` cấu hình hàng đợi nhưng không giới hạn số lượng worker.
     - Không có cơ chế dọn dẹp file xuất tệp dở dang (partial file) trên đĩa khi tiến trình xuất tệp gặp sự cố.
   - **Giải pháp:**
     - Cấu hình tường minh `options.WorkerCount` cho cả `ReportService` và `ExportService` với giá trị đọc từ cấu hình `Hangfire:WorkerCount` (mặc định 2, chặn cận an toàn trong khoảng 1 đến 8 workers).
     - Trong `ExportFileGenerator.cs` và `ExportGenerationJob.cs`: Thêm khối dọn dẹp tự động xóa tệp xuất dở dang trên đĩa khi tác vụ xuất file bị lỗi hoặc bị hủy.

3. **Khắc phục lỗi LOW: File preview báo cáo lưu ngoài volume mount, mất khi recreate container (`OPS-F07`):**
   - **Hiện trạng trước sửa đổi:**
     - Trong `docker-compose.yml`, `report-service` chỉ mount volumes `report_attachments` và `report_uploads`. Thư mục `/app/report-previews` không được mount, khiến toàn bộ các file PDF xem trước đã tạo biến mất khi container được cập nhật hoặc tái tạo.
   - **Giải pháp:**
     - Khai báo persistent volume `report_previews` trong `docker-compose.yml` và mount vào `/app/report-previews` cho `report-service`.
     - Cấu hình biến môi trường `Uploads__PreviewStoragePath: "/app/report-previews"` và bổ sung key cấu hình `Uploads:PreviewStoragePath` vào `appsettings.json`.

4. **Khắc phục lỗi LOW: Lỗi deserialize export snapshot bị bỏ qua âm thầm (`CQ-F01`):**
   - **Hiện trạng trước sửa đổi:**
     - Trong `ExportFileGenerator.cs`, cả ba phương thức `GeneratePdf`, `GenerateExcel`, `GenerateDocx` đều bọc lệnh `JsonSerializer.Deserialize` trong khối `try { ... } catch { }` rỗng. Khi chuỗi JSON snapshot bị hỏng, lỗi bị nuốt mất hoàn toàn và hệ thống vẫn tạo ra các file rỗng không có dữ liệu báo cáo, rồi chuyển trạng thái thành `Completed`.
   - **Giải pháp:**
     - Thay thế khối try/catch nuốt lỗi bằng phương thức tập trung `ParseSnapshot(ExportRequest request)`. Nếu `SnapshotJson` có dữ liệu nhưng không thể giải mã JSON hoặc trả về null, phương thức ném ngoại lệ `InvalidOperationException` tường minh kèm thông điệp chi tiết.
     - Trong `ExportGenerationJob.cs`: Bắt ngoại lệ, ghi nhận trạng thái `ExportStatuses.Failed`, lưu thông điệp lỗi cụ thể ("Dữ liệu snapshot báo cáo không hợp lệ..."), xóa file rác dở dang và không phát sinh sự kiện outbox sai lệch.

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Services/ReportService/Extensions/ReportExtensions.cs` | Sửa đổi | Stream upload trực tiếp với IncrementalHash SHA-256; bổ sung `ValidateArchiveDecompression` kiểm tra zip-bomb & traversal |
| 2 | `src/Services/ReportService/Endpoints/ReportFileEndpoints.cs` | Sửa đổi | Bắt ngoại lệ `InvalidOperationException` từ `SaveUploadedReportFileAsync` và trả lỗi 400 Bad Request rõ ràng |
| 3 | `src/Services/ReportService/Services/ReportPreviewGenerator.cs` | Sửa đổi | Stream DOCX trực tiếp từ FileStream (bỏ ReadAllBytesAsync); giới hạn 200 phần tử xem trước; dọn dẹp partial preview file |
| 4 | `src/Services/ReportService/Program.cs` | Sửa đổi | Cấu hình tường minh `WorkerCount` cho Hangfire Server (1..8, mặc định 2) |
| 5 | `src/Services/ReportService/appsettings.json` | Sửa đổi | Thêm cấu hình `Uploads:StoragePath`, `Uploads:PreviewStoragePath`, và `Hangfire:WorkerCount` |
| 6 | `src/Services/ExportService/Services/ExportFileGenerator.cs` | Sửa đổi | Thêm `ParseSnapshot` ném ngoại lệ tường minh khi JSON snapshot hỏng (CQ-F01); dọn dẹp file xuất dở dang khi lỗi |
| 7 | `src/Services/ExportService/Services/ExportGenerationJob.cs` | Sửa đổi | Ghi nhận lỗi cụ thể từ snapshot và xóa file rác dở dang khi lỗi |
| 8 | `src/Services/ExportService/Program.cs` | Sửa đổi | Cấu hình tường minh `WorkerCount` cho Hangfire Server (1..8, mặc định 2) |
| 9 | `src/Services/ExportService/appsettings.json` | Sửa đổi | Thêm cấu hình `Hangfire:WorkerCount` |
| 10 | `docker-compose.yml` | Sửa đổi | Bổ sung volume `report_previews:/app/report-previews`, biến môi trường `Uploads__PreviewStoragePath`, và định nghĩa volume ở root |
| 11 | `tests/Backend.StabilizationTests/DocumentProcessingAndJobHardeningTests.cs` | Tạo mới | Bộ 12 test cases chuyên biệt kiểm thử streaming upload, zip-bomb, docx preview bounds, snapshot error handling, Hangfire worker, và docker volumes |

---

## 3. Kết quả Kiểm thử Hồi quy (Verification Results)

1. **Bộ test chuyên biệt Phase 15 (`DocumentProcessingAndJobHardeningTests`):**
   - Đạt **12/12 tests PASSED (100%)**:
     - `SaveUploadedReportFileAsync_StreamsDirectly_ComputesAccurateSha256`: Stream upload trực tiếp và tính toán chính xác SHA-256 không cấp phát byte array trong RAM.
     - `ValidateArchiveDecompression_ValidZip_PassesValidation`: Xác thực DOCX/XLSX zip hợp lệ đi qua trơn tru.
     - `ValidateArchiveDecompression_ExcessiveEntries_ThrowsInvalidOperationException`: Từ chối archive vượt quá 500 entries (chống zip-bomb).
     - `ValidateArchiveDecompression_ZipSlipTraversal_ThrowsInvalidOperationException`: Từ chối archive chứa đường dẫn traversal `../` (chống zip-slip).
     - `ValidateArchiveDecompression_NonArchiveExtension_IsIgnored`: Bỏ qua các tệp không phải archive (PDF) an toàn.
     - `ReportPreviewGenerator_DocxPreview_BoundsElementsAndStreams`: Render DOCX preview bằng FileStream streaming và giới hạn 200 phần tử.
     - `ReportPreviewGenerator_NonExistentFile_MarksFailedAndCleansUp`: Đánh dấu trạng thái Failed và dọn dẹp an toàn khi file không tồn tại.
     - `ExportFileGenerator_MalformedSnapshotJson_ThrowsInvalidOperationException`: Ném ngoại lệ khi snapshot JSON hỏng, không nuốt lỗi âm thầm.
     - `ExportFileGenerator_NullOrEmptySnapshot_SucceedsGracefully`: Xử lý hợp lệ trường hợp snapshot rỗng/null theo fallback định trước.
     - `ExportGenerationJob_MalformedSnapshot_SetsFailedStatusAndCleansUp`: Cập nhật trạng thái Failed, ghi log lỗi, xóa partial file và không sinh outbox message.
     - `HangfireServer_Configuration_BoundsWorkerCount`: Xác thực thuật toán chặn worker count (1..8) theo RAM container.
     - `DockerCompose_ReportService_HasPersistentPreviewVolumeMounted`: Xác thực cấu hình mount volume `report_previews` trong docker-compose.yml.

2. **Kiểm tra toàn bộ Solution:**
   - `dotnet test ClubReportHub.sln -c Release`: **186/186 tests PASSED (100%)**
     - `AdminService.IntegrationTests`: 28/28 passed.
     - `Backend.StabilizationTests`: 158/158 passed (bao gồm cả 12 test mới của Phase 15).
   - `dotnet test tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj -c Release`: **39/39 tests PASSED (100%)**
   - **Tổng cộng: 225/225 tests PASSED (0 lỗi, 0 skipped).**

3. **Kiểm tra Biên dịch & Docker Compose:**
   - `dotnet build ClubReportHub.sln -c Release -warnaserror`: **0 Warnings, 0 Errors.**
   - `docker compose config --quiet`: **Cấu hình YAML hợp lệ (Exit code 0).**
