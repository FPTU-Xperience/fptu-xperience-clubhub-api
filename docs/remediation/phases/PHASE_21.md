# PHASE 21 — FINAL INDEPENDENT REVIEW FIXES

## Mục tiêu

Phase 21 xử lý các khoảng trống được phát hiện khi review độc lập sau Phase 20. Phạm vi chỉ gồm refresh-token security, Report transaction atomicity, Outbox batch recovery, độ ổn định của test concurrency và quality gate. Dev-login được giữ nguyên để frontend phục vụ demo đồ án theo accepted risk `SEC-F01`.

## Thay đổi đã triển khai

### SEC-F08 / SEC-F09 — Refresh token

- Không còn cho phép giá trị SHA-256 đang lưu trong database được gửi trực tiếp như bearer credential.
- Plaintext lookup chỉ còn dùng cho token legacy không có định dạng SHA-256 64 ký tự.
- Không trả `ReplacedByToken` dạng hash cho client khi cache grace-period không còn raw token.
- Chỉ bắt `DbUpdateConcurrencyException` trong race rotation. Lỗi database khác không bị chuyển thành response thành công giả.
- Concurrent loser chờ ngắn để lấy response cache của winner; nếu không có raw token an toàn thì trả invalid.

### DATA-F03 — Report atomicity

- Create Report ghi Report, Outbox và Audit trong cùng relational transaction.
- Upload Report ghi Report/File và Audit trong cùng relational transaction.
- Khi transaction upload rollback, file upload và preview đã tạo được dọn khỏi storage nếu có thể.
- Thêm SQLite regression test ép lần lưu audit/outbox thất bại và xác nhận Report, Outbox, Audit đều rollback.

### REL-F02 — Outbox batch continuation

- Khi cập nhật trạng thái sau publish gặp `DbUpdateConcurrencyException`, entity lỗi được detach khỏi DbContext.
- Message tiếp theo trong cùng batch tiếp tục lưu trạng thái độc lập, tránh publish lại do entry cũ làm hỏng mọi `SaveChangesAsync` sau đó.
- Thêm regression test mô phỏng post-publish conflict và xác nhận batch tiếp tục.

### Test và quality gate

- Test concurrent Create Report dùng SQLite file-backed để unique index thực sự được thực thi; loại bỏ flake của EF InMemory provider.
- Full solution formatting đạt `dotnet format --verify-no-changes`.
- `git diff --check` đạt, không còn trailing whitespace hoặc blank line thừa.

## Acceptance evidence

| Kiểm tra | Kết quả |
| :--- | :---: |
| `dotnet build ClubReportHub.sln -c Release -warnaserror` | PASSED — 0 warning, 0 error |
| `dotnet test ClubReportHub.sln -c Release --no-build` | PASSED — 287 passed, 1 skipped, 0 failed |
| `dotnet format ClubReportHub.sln --verify-no-changes --no-restore` | PASSED |
| EF migration drift — 8/8 services | PASSED |
| Dependency vulnerability scan — 15/15 projects | PASSED |
| `docker compose config --quiet` | PASSED |
| `git diff --check` | PASSED |

Live SQL migration execution và full Docker runtime E2E vẫn phụ thuộc môi trường có SQL Server/Docker daemon; model drift và Compose configuration đã được xác nhận.
