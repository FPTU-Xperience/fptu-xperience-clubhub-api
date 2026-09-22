# BÁO CÁO HOÀN THÀNH PHASE 06 — CROSS-SERVICE WORKFLOW AUTHORIZATION

> **Thời điểm hoàn tất:** 2026-09-21  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi MEDIUM giả mạo Header quy trình liên dịch vụ (`SEC-F04`):**
   - **Bảo vệ Endpoint Review Ngân sách tại `FinanceService`:**
     - Cập nhật `ProposalEndpoints.cs`: Bổ sung hàm `ValidateCombinedReportWorkflowAsync` kiểm tra bắt buộc đối với tất cả đề xuất ngân sách có `SourceReportId`.
     - Chặn đứng mọi hành vi duyệt trực tiếp của client bên ngoài mà không thông qua quy trình duyệt báo cáo của `ReportService`:
       - `manager-approve`: Bắt buộc token nội bộ hợp lệ và báo cáo tương ứng thuộc cùng câu lạc bộ.
       - `manager-reject`: Bắt buộc xác thực workflow kết hợp.
       - `approve` (chủ nhiệm / cán bộ duyệt cấp cao): Bắt buộc kiểm tra token nội bộ và đối chiếu trạng thái báo cáo gốc.
       - `reject`: Bắt buộc kiểm tra workflow kết hợp.
   - **Xác thực Secret Nội bộ Bằng Phép So Sánh Thời Gian Cố Định (`FinanceExtensions`):**
     - Tại `FinanceExtensions.IsCombinedReportWorkflow`:
       - Kiểm tra cả header `X-Combined-Report-Workflow: true` và `X-Internal-Workflow-Token`.
       - Nếu thiếu token nội bộ hoặc token rỗng: Lập tức từ chối (`false`).
       - Dùng `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` so khớp chuỗi byte UTF-8 của token với cấu hình `InternalServiceAuth:Secret` / `Security:InternalWorkflowToken` nhằm triệt tiêu hoàn toàn nguy cơ tấn công Timing Attack.
   - **Gửi Token Xác Thực Nội Bộ Từ `ReportService`:**
     - Cập nhật `FinanceWorkflowClient.cs`: Khi `ReportService` kích hoạt các hành động duyệt/từ chối ngân sách liên kết (`manager-approve`, `manager-reject`, `approve`, `reject`), tự động đính kèm `X-Internal-Workflow-Token` nạp từ cấu hình `InternalServiceAuth:Secret`.

2. **Khắc phục lỗi HIGH liên kết ngân sách chéo câu lạc bộ (`SEC-F05`):**
   - **Thêm Client Truy Vấn Proposal Chi Tiết Trong `ReportService`:**
     - Trong `FinanceWorkflowClient.cs`, bổ sung phương thức `GetProposalAsync(proposalId, bearerToken, ct)`.
     - Trả về snapshot gồm `Id`, `ClubId`, `SourceReportId`, `Status`, `RequestedAmount`, `ApprovedAmount`.
   - **Ràng Buộc Chặt Chẽ Tại `ReportWorkflowEndpoints.LinkFutureEventBudget`:**
     - Trước khi gán `report.BudgetProposalId = request.BudgetProposalId`, gọi `financeWorkflow.GetProposalAsync`:
       - Nếu không tìm thấy proposal: Trả về HTTP 400 (`"Budget proposal not found."`).
       - Nếu `proposal.ClubId != report.ClubId`: Trả về HTTP 400 (`"The budget proposal belongs to another club."`).
       - Nếu `proposal.SourceReportId` đã được gán và khác `report.Id`: Trả về HTTP 400 (`"The budget proposal is not associated with this report."`).

3. **Thẩm định & Khắc phục lỗi Tạo Activity từ Báo Cáo Không Hợp Lệ (`POT-F04`):**
   - **Xác nhận trạng thái:** `VERIFIED` — Đã phòng vệ và xác thực độc lập tại `ActivityService`.
   - **Tạo mới `ReportVerificationClient`:**
     - Đặt tại `src/Services/ActivityService/Infrastructure/ReportVerificationClient.cs`.
     - Đăng ký typed `HttpClient` trong `ActivityService/Extensions/ServiceCollectionExtensions.cs` trỏ tới `Services:ReportService:BaseUrl`.
   - **Xác Thực Độc Lập Tại `POST /api/activities/from-approved-report`:**
     - Trong `ActivityEndpoints.cs`, trước khi tạo hoặc tái sử dụng activity:
       - Kiểm tra báo cáo tồn tại: nếu không tìm thấy, trả về HTTP 400 (`"Report not found."`).
       - Kiểm tra quyền sở hữu CLB: nếu `report.ClubId != request.ClubId`, trả về HTTP 400 (`"The report belongs to another club."`).
       - Kiểm tra trạng thái phê duyệt: chỉ cho phép báo cáo có `Status == "Approved"`, nếu chưa duyệt trả về HTTP 400 (`"Only approved reports can be published as activities."`).
       - Kiểm tra chi tiết hoạt động: bắt buộc `report.Details.Any(d => d.Id == request.ReportDetailId)`, ngăn chặn việc chỉ định detail ID tùy tiện hoặc không thuộc báo cáo.

4. **Chuẩn hóa Cấu hình Base URL và Docker Compose (`ARCH-F03`, `ARCH-F04`):**
   - **`docker-compose.yml`:**
     - Bổ sung `Services__ReportService__BaseUrl: "http://report-service:8080/"` cho `activity-service`.
     - Bổ sung `Services__FinanceService__BaseUrl: "http://finance-service:8080/"` và `Services__ActivityService__BaseUrl: "http://activity-service:8080/"` cho `report-service`.
   - **`ExportService/Program.cs`:**
     - Thay thế hardcoded `http://report-service:8080` bằng cấu hình `builder.Configuration["Services:ReportService:BaseUrl"] ?? "http://localhost:5103"`, cho phép chạy mượt mà ở cả local lẫn Docker.

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Services/FinanceService/Extensions/FinanceExtensions.cs` | Modified | `IsCombinedReportWorkflow`: Yêu cầu token nội bộ non-empty, so sánh `FixedTimeEquals`. |
| 2 | `src/Services/FinanceService/Endpoints/ProposalEndpoints.cs` | Modified | `ValidateCombinedReportWorkflowAsync`: Kiểm tra token + đối chiếu report club & budget proposal ID. |
| 3 | `src/Services/ReportService/Clients/FinanceWorkflowClient.cs` | Modified | Bổ sung `GetProposalAsync`, đính kèm `X-Internal-Workflow-Token`. |
| 4 | `src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs` | Modified | `LinkFutureEventBudget`: Kiểm tra proposal tồn tại, cùng CLB và đúng report ID. |
| 5 | `src/Services/ActivityService/Infrastructure/ReportVerificationClient.cs` | **Created** | HTTP Client xác thực báo cáo từ `ReportService`. |
| 6 | `src/Services/ActivityService/Extensions/ServiceCollectionExtensions.cs` | Modified | Đăng ký `ReportVerificationClient` với cấu hình base URL. |
| 7 | `src/Services/ActivityService/Endpoints/ActivityEndpoints.cs` | Modified | Xác thực trạng thái Approved, ClubId, DetailId trước khi tạo activity. |
| 8 | `src/Services/ExportService/Program.cs` | Modified | Đọc `Services:ReportService:BaseUrl` từ config thay vì hardcoded host Docker. |
| 9 | `docker-compose.yml` | Modified | Bổ sung environment base URLs cho inter-service communication. |
| 10 | `tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj` | Modified | Tham chiếu `FinanceService` và `ActivityService`. |
| 11 | `tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.cs` | **Created** | 13 regression tests cho SEC-F04, SEC-F05, POT-F04. |
| 12 | `docs/remediation/FINDINGS_LEDGER.md` | Modified | Đánh dấu FIXED cho SEC-F04, SEC-F05, ARCH-F03, ARCH-F04; VERIFIED cho POT-F04. |
| 13 | `docs/remediation/TEST_MATRIX.md` | Modified | Ánh xạ 13 test cases mới vào ma trận kiểm thử. |
| 14 | `docs/remediation/CHECKPOINT.md` | Modified | Cập nhật checkpoint Phase 06 hoàn tất. |

---

## 3. Kết quả kiểm thử tự động (Verification Results)

1. **Kiểm tra biên dịch toàn bộ Solution:**
   ```bash
   dotnet build ClubReportHub.sln -c Release -warnaserror
   ```
   *Kết quả:* **0 Warning, 0 Error** (Build Succeeded).

2. **Kiểm tra bộ test hồi quy chuyên biệt Phase 06:**
   ```bash
   dotnet test tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj -c Release --filter "FullyQualifiedName~CrossServiceWorkflowAuthorizationTests"
   ```
   *Kết quả:* **13/13 tests PASSED (100%)**:
   - `IsCombinedReportWorkflow_WithoutInternalToken_ReturnsFalse_WhenTokenConfigured`
   - `IsCombinedReportWorkflow_WithMismatchedInternalToken_ReturnsFalse`
   - `IsCombinedReportWorkflow_WithValidInternalToken_ReturnsTrue`
   - `Finance_DirectClientReview_OnReportLinkedProposal_WithoutInternalToken_IsRejected`
   - `Finance_CombinedWorkflowReview_WithMismatchedReportClub_IsRejected`
   - `Report_LinkBudget_CrossClubProposal_IsRejected`
   - `Report_LinkBudget_MismatchedSourceReportId_IsRejected`
   - `Report_LinkBudget_ValidProposal_Succeeds`
   - `Activity_CreateFromApprovedReport_ReportNotFound_IsRejected`
   - `Activity_CreateFromApprovedReport_MismatchedClub_IsRejected`
   - `Activity_CreateFromApprovedReport_UnapprovedReport_IsRejected`
   - `Activity_CreateFromApprovedReport_DetailNotFound_IsRejected`
   - `Activity_CreateFromApprovedReport_Valid_CreatesActivitySuccessfully`

3. **Kiểm tra bộ kiểm thử toàn hệ thống (Zero Regression):**
   ```bash
   dotnet test ClubReportHub.sln -c Release
   ```
   *Kết quả:* **106/106 test runner executions PASSED** (78 Backend.StabilizationTests + 28 AdminService.IntegrationTests).

4. **Kiểm tra cấu hình Docker Compose:**
   ```bash
   docker compose config --quiet
   ```
   *Kết quả:* **Hợp lệ (Exit code 0)**.
