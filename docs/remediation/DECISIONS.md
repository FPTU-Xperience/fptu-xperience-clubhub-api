# NHẬT KÝ QUYẾT ĐỊNH THIẾT KẾ & KIẾN TRÚC (DECISIONS RECORD)

> Tài liệu này ghi lại các Architectural Decision Records (ADR) và quyết định kỹ thuật/nghiệp vụ trong suốt quá trình remediation dự án ClubReportHub Backend.

---

## ADR-000: Giữ nguyên kiến trúc hiện tại và phạm vi đồ án tốt nghiệp
- **Ngày quyết định:** 2026-09-21
- **Bối cảnh:** Báo cáo audit chỉ ra một số coupling và kiến trúc vertical slice thay vì Clean Architecture.
- **Quyết định:** Giữ nguyên stack cốt lõi: ASP.NET Core 8 Minimal APIs, YARP API Gateway, SQL Server 2022 (Database-per-service), Redis Streams, Hangfire, gRPC. Không đưa thêm Kafka, RabbitMQ, Kubernetes, hay rewrite sang Clean Architecture/Monolith.
- **Lý do:** Tối ưu hóa thời gian và nguồn lực, tránh over-engineering, tập trung khắc phục triệt để các lỗi bảo mật (Security), toàn vẹn dữ liệu (Data Invariants) và tính bền bỉ (Reliability).

---

## ADR-001: Phân tách công việc theo mô hình 6 Waves / 21 Phases
- **Ngày quyết định:** 2026-09-21
- **Bối cảnh:** Lượng findings lớn (58 confirmed) đòi hỏi chia nhỏ để các AI coding tools có thể thực hiện độc lập theo từng session mà không bị quá tải context.
- **Quyết định:** Triển khai theo [CLUBHUB_REMEDIATION_MASTER_PLAN.md](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/docs/plan/CLUBHUB_REMEDIATION_MASTER_PLAN.md) chia làm 6 Waves:
  - Wave 0: Baseline & Remediation Setup (Phase 00)
  - Wave 1: Critical Security & Availability (Phase 01–05)
  - Wave 2: Authorization & Business Integrity (Phase 06–09)
  - Wave 3: Database & Reliability Reconstruction (Phase 10–14)
  - Wave 4: Performance & Code Quality (Phase 15–17)
  - Wave 5: Deployment, Testing & Final Acceptance (Phase 18–20)
- **Hệ quả:** Mỗi phase có một báo cáo riêng trong `docs/remediation/phases/` và cập nhật vào `CHECKPOINT.md`.

---

## ADR-002: Bảo vệ trạng thái working tree hiện hữu
- **Ngày quyết định:** 2026-09-21
- **Bối cảnh:** Repository tại HEAD commit `33bf7fe` có một số thay đổi chưa commit từ trước audit (`M AGENTS.md`, `D GEMINI.md`, `D scripts/run-gemini-task.ps1`) cùng các thư mục `docs/audit/`, `docs/plan/`.
- **Quyết định:** Tuyệt đối không tự ý `git restore`, `git reset`, hoặc xóa các file/thư mục này. Toàn bộ tài liệu audit và plan được xem là tài sản tri thức của dự án.

---

## ADR-003: Nguyên tắc kiểm thử cho Security và Data Integrity
- **Ngày quyết định:** 2026-09-21
- **Bối cảnh:** Một số lỗ hổng bảo mật nghiêm trọng (`SEC-F01`, `SEC-F10`, `REL-F03`) và race condition ở database (`DATA-F01`, `DATA-F02`, `DATA-F05`) thiếu hoàn toàn test tự động.
- **Quyết định:** Mọi bản vá thuộc nhóm CRITICAL và HIGH bắt buộc phải có regression test chứng minh lỗi đã được ngăn chặn trước khi nghiệm thu phase. Đối với các kiểm thử concurrency và unique constraints, không chỉ dựa vào EF InMemory hay SQLite mà cần định nghĩa rõ ràng điều kiện chạy trên SQL Server.

---

## ADR-004: Giữ lại Dev Login cho nhóm kiểm thử (Accepted Risk / Graduation-Demo Exception)
- **Ngày quyết định:** 2026-09-21 (Phase 01, rà soát lại Phase 12)
- **Bối cảnh:** Audit ghi nhận `SEC-F01` (Anonymous dev-login tại Production). Tuy nhiên, người dùng yêu cầu giữ lại endpoint này để thành viên nhóm kiểm thử giao diện và API mà không bị phụ thuộc vào Google OAuth credentials trên môi trường triển khai của đồ án.
- **Quyết định:**
  1. Giữ nguyên endpoint `/api/auth/dev-login` hoạt động bình thường, chấp nhận `SEC-F01` là **Accepted Risk**.
  2. Xác định rõ đây là một **ngoại lệ có chủ đích dành riêng cho mục đích demo và bảo vệ đồ án tốt nghiệp** (deliberate graduation-demo exception), giúp hội đồng chấm thi và nhóm kiểm thử có thể duyệt nhanh toàn bộ các vai trò người dùng (Admin, Manager, Member...) mà không phụ thuộc vào cấu hình Google Cloud Console OAuth thật.
  3. Bắt buộc phải tắt hoàn toàn (`ENABLE_DEV_LOGIN=false`, `Auth__EnableDevLogin=false`) trước bất kỳ lần triển khai hoặc phát hành thực tế nào ra môi trường Internet công cộng (real production exposure).
  4. Sửa `SEC-F02`: Đồng bộ cấu hình `ENABLE_DEV_LOGIN` và `Auth__EnableDevLogin` vào `appsettings.json` và `docker-compose.yml` (mặc định `true` cho môi trường đồ án).
  5. Bổ sung structured audit logging khi dev-login được kích hoạt để có dấu vết kiểm toán (`OBS-F02`).

---

## ADR-005: Xử lý `ARCH-F01` — Giải quyết Dependency Cycles bằng Source-of-Truth phân định, Resilient Fallback & Transactional Outbox
- **Ngày quyết định:** 2026-09-22 (Phase 20)
- **Bối cảnh:** Báo cáo audit ghi nhận `ARCH-F01` (Vòng lặp phụ thuộc runtime giữa `ClubService ↔ ActivityService` và `ReportService ↔ FinanceService`). Yêu cầu đặt ra là giải quyết nguy cơ cascade failure và coupling mà không over-engineer đồ án tốt nghiệp sang Event Sourcing hoặc Saga phức tạp.
- **Quyết định:**
  1. **Phân định ranh giới Source-of-Truth (Single Ownership):**
     - `ClubService`: Nguồn chân lý duy nhất về CLB, thành viên, và phân quyền quản lý (`Club`, `ClubMembership`, `ClubManagerAssignment`).
     - `ActivityService`: Nguồn chân lý duy nhất về hoạt động và dữ liệu điểm danh (`ClubActivity`, `ActivityAttendance`).
     - `ReportService`: Nguồn chân lý duy nhất về đề xuất/báo cáo và quy trình phê duyệt (`Report`, `ReportWorkflow`).
     - `FinanceService`: Nguồn chân lý duy nhất về ngân sách và quyết toán tài chính (`BudgetProposal`, `ExpenseSettlement`).
  2. **Cách ly lỗi & Graceful Degradation (Read Queries):**
     - Trong `ClubService` (`MemberManagementEndpoints.cs`), khi truy vấn chỉ số tham gia hoạt động của thành viên qua `ActivityStatisticsClient`, nếu `ActivityService` gặp sự cố mạng hoặc timeout, service ghi log cảnh báo và trả về chỉ số mặc định (0 tham gia) thay vì trả về `503 Service Unavailable`. Nhờ vậy, Chủ nhiệm CLB vẫn xem và quản trị danh sách thành viên bình thường ngay cả khi dịch vụ hoạt động gặp sự cố.
  3. **Event-Driven Asynchronous Handoff (Write Operations):**
     - Mọi thay đổi trạng thái liên dịch vụ (phê duyệt báo cáo, duyệt đề xuất ngân sách, điểm danh, tạo hoạt động) được xuất bản bất đồng bộ qua Redis Streams và được bảo toàn độ tin cậy bằng **Transactional Outbox Pattern** (`OutboxMessage` lease-claiming) ở cả `ReportService`, `FinanceService`, `ClubService` và `ActivityService`. Không sử dụng 2-Phase Commit đồng bộ.
  4. **Kết luận:** Finding `ARCH-F01` được phân loại là **FIXED** và thỏa mãn hoàn toàn tiêu chuẩn kiến trúc cho đồ án tốt nghiệp.


