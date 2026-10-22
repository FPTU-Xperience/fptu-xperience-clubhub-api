# Bàn giao BE: thư viện liên kết hoạt động — 23/10/2026

Contract nguồn: FE main `3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42`, `docs/contracts/activity-profile-resources-api.md`. Trạng thái tổng nhóm: **HOÀN THÀNH MỘT PHẦN**. Phạm vi đã triển khai là thư viện liên kết `EMBED` có persistence; tài liệu này không đánh dấu hoàn thành upload tài liệu/video, tải tệp riêng hay public transport. Chỉ thay BE.

## API và actor

| API | Trạng thái / actor |
| --- | --- |
| `GET /api/activities/{activityId}/resources` | **Đã triển khai cho EMBED**. Quản lý hiện tại, thành viên đã duyệt đọc tài nguyên của CLB; người cùng campus chỉ đọc `PUBLIC` khi hoạt động có `CAMPUS_PUBLIC` và publication `PUBLISHED`/`CLOSED`. CTSV/Admin chỉ đọc phần PUBLIC trong phạm vi; không tự có quyền sửa hoặc đọc CLUB. |
| `POST /api/activities/{activityId}/resources` | **Đã triển khai EMBED; DOCUMENT/MEDIA chưa hoàn thành**. Chỉ quản lý đúng CLB hiện tại được tạo; không suy quyền từ nhãn BQL phía FE. |
| `PATCH /api/activities/{activityId}/resources/{resourceId}` | **Đã triển khai** tiêu đề, thứ tự, visibility; cần version và quản lý hiện tại. |
| `DELETE /api/activities/{activityId}/resources/{resourceId}` | **Đã triển khai gỡ có lịch sử**; body chứa version/lý do. Không xóa dữ liệu lịch sử. |
| `GET /api/activities/{activityId}/resources/history?page=1&pageSize=50` | **Đã triển khai**; chỉ quản lý hiện tại; phân trang tối đa 100, mới nhất trước. |
| `GET /api/activities/{id}` | **Đã bổ sung projection ở detail**: `resources`, `resourceCapabilities`, `resourceAvailability`. Các trường và policy hiện có được giữ; danh sách hoạt động không tải toàn bộ gallery. |
| `POST /api/activities/{id}/resources/upload-intents` | **CHƯA TRIỂN KHAI upload**. Có phản hồi `501 RESOURCE_UPLOAD_NOT_IMPLEMENTED` sau kiểm tra quyền; đây không phải operation hoàn thành, không phát URL tải lên/receipt. |
| `GET /api/public/activities/{id}` | **CHƯA TRIỂN KHAI**. `CAMPUS_PUBLIC` yêu cầu tài khoản cùng campus, không đồng nghĩa xem ẩn danh. Chưa bổ sung chính sách xuất bản ẩn danh. |

Mọi route thư viện đều yêu cầu JWT và quyền từ nguồn hiện tại: tài khoản active/unlocked, CLB active, campus, membership và management. Quyền được kiểm tra trước cả khi phát lại kết quả idempotent. Quản lý CLB A không sửa hoạt động B; resource ID phải thuộc đúng activity ID. Hoạt động draft chỉ quản lý hoặc reviewer có phạm vi được đọc; hoạt động hủy giữ lịch sử đọc và chặn thay đổi mới.

Hiện chưa cấp thêm quyền CONTENT/event specialist từ nhóm governance mới. Report BQL không cấp quyền. Quản lý hiện tại dùng nguồn quyền đã có; cutover governance vẫn là phần riêng chưa hoàn thành.

## Request/response cho FE

Tạo, sửa và gỡ bắt buộc header `Idempotency-Key` (1–100 ký tự ASCII có thể in, không khoảng trắng). Gửi lại cùng key/body trả đúng receipt; cùng key khác body trả `409 IDEMPOTENCY_KEY_REUSED`. Mỗi operation có phạm vi actor + HTTP method/path. Quyền bị thu hồi vẫn bị từ chối khi gửi lại.

```http
POST /api/activities/123/resources
Content-Type: application/json
Idempotency-Key: draft-link-123

{"kind":"EMBED","title":"Tài liệu workshop","visibility":"CLUB","url":"https://docs.google.com/document/d/example/edit"}
```

`visibility` bỏ qua mặc định `CLUB`; giá trị hợp lệ là `PUBLIC`/`CLUB`. `kind` phải là `EMBED`; không nhận `uploadIntentId` cho liên kết. `position` có thể bỏ qua để thêm cuối, hoặc số nguyên 0–9999. Tối đa 100 tài nguyên đang gắn trong một hoạt động, sắp xếp theo position rồi ID. Tiêu đề 1–160 ký tự sau trim, không chứa control characters; URL tối đa 2000 ký tự. Body JSON tối đa 256 KiB, không nhận field lạ, field trùng kể cả khác hoa/thường, key lưu trữ, iframe HTML hay blob URL.

Response tạo là `201`, có `id` GUID ổn định, `version` GUID, `kind`, `title`, `visibility`, `url`, `provider`, `position`, `createdAtUtc`, `updatedAtUtc`. `uploadedByUserId` chỉ có trong projection dành cho quản lý. Gallery trả:

```json
{"items":[],"availability":"AVAILABLE","resourceCapabilities":{"canManage":true,"canUpload":false,"supportedKinds":["EMBED"],"uploadStatus":"NOT_IMPLEMENTED"}}
```

FE chỉ bật lưu liên kết khi `canManage=true` và có `EMBED` trong `supportedKinds`; **không bật upload tệp** vì `canUpload=false`. Có `canManage` không có nghĩa đã hỗ trợ mọi kind. Gallery rỗng với `AVAILABLE` là kết quả có thẩm quyền, khác với không xác minh được nguồn.

Detail trả `resourceAvailability:"AVAILABLE"` sau xác minh nguồn. Khi nguồn quyền không sẵn sàng, trả `resources:null`, capabilities false và `AUTHORITY_UNAVAILABLE`; khi bị từ chối quyền tài nguyên thì `ACCESS_DENIED`. Endpoint gallery riêng trả lỗi 403/503 tương ứng, không giả mảng rỗng. Endpoint activity detail vẫn dùng chính sách đọc activity hiện tại; chưa mở rộng nó cho campus nonmember. Người chỉ xem cùng campus dùng route discovery hiện có và gallery read có phạm vi nêu trên.

```http
PATCH /api/activities/123/resources/RESOURCE_GUID
Idempotency-Key: edit-resource-123
Content-Type: application/json

{"expectedVersion":"CURRENT_VERSION_GUID","title":"Tiêu đề mới","position":2}
```

Trường bỏ qua giữ nguyên; `null` bị từ chối cho title/visibility/position. Không sửa URL/kind qua PATCH; tạo liên kết mới rồi gỡ liên kết cũ. Version sai trả `409 STALE_RESOURCE`; CAS và transaction bảo vệ hai người sửa cùng lúc. Thứ tự trùng hợp lệ và luôn có ID làm khóa phụ ổn định.

```http
DELETE /api/activities/123/resources/RESOURCE_GUID
Idempotency-Key: detach-resource-123
Content-Type: application/json

{"expectedVersion":"CURRENT_VERSION_GUID","reason":"Thay tài liệu cập nhật"}
```

Lý do 1–500 ký tự sau trim. Gỡ trả 200 với id/version/detachedAtUtc; tài nguyên không còn trong gallery nhưng audit CREATE/UPDATE/DETACH và snapshot trước/sau còn nguyên. Resource audit chỉ append, DbContext từ chối sửa/xóa. Mutation, audit và idempotency receipt cùng transaction; payload không hợp lệ không để lại thay đổi dở dang. Kiểm tra trạng thái hủy được thực hiện lại trong transaction.

## Liên kết và quyền riêng tư

Chỉ HTTPS, cổng 443, không credentials, không IP literal và chỉ hostname chính xác trong danh sách hỗ trợ. Hiện có YouTube (`youtube.com`, `www.youtube.com`, `m.youtube.com`, `youtu.be`, `youtube-nocookie.com`, `www.youtube-nocookie.com`) cùng các liên kết external `docs.google.com`, `drive.google.com`, `forms.gle`, `onedrive.live.com`, `1drv.ms`, `github.com`, `www.figma.com`, `www.canva.com`. Chưa hỗ trợ host khác/subdomain tự do; thêm provider phải cập nhật policy và kiểm thử, không dùng wildcard.

YouTube phải có video ID hợp lệ 11 ký tự và định dạng watch/embed/shorts hoặc youtu.be; lưu URL chuẩn `https://www.youtube-nocookie.com/embed/{videoId}`, bỏ tham số autoplay/tracking. BE không tải URL từ xa, không tạo thumbnail/preview bằng việc fetch URL bất kỳ. `provider:EXTERNAL` mở như liên kết, không có HTML nhúng. FE chỉ bật player YouTube khi người dùng yêu cầu xem theo contract.

`PUBLIC` ở tài nguyên chỉ là phạm vi trong activity mà người gọi được phép đọc; chưa có route ẩn danh. `CLUB` URL không trả cho người ngoài CLB, kể cả campus visitor/reviewer chỉ có quyền xem PUBLIC. Nó không thay ACL của Google Drive/nhà cung cấp ngoài: chủ CLB vẫn phải cấu hình quyền truy cập tại nguồn; chưa có private file delivery do BE quản lý. API không nhận tài liệu riêng để gửi đến dịch vụ preview bên thứ ba.

## Migration, kiểm thử và phần còn lại

Migration ActivityService: `20261022174514_ActivityEmbeddedResources`, thêm Resource/Audit có FK và index, không sửa dữ liệu hoạt động cũ. Migration đã được kiểm tra nâng từ schema `20261022090451_ActivityActionWorkflowWithoutScoring` với hoạt động có sẵn. SQL test sử dụng database tạm riêng, lấy kết nối CI từ `ADMIN_SERVICE_TEST_CONNECTION_STRING` hoặc LocalDB mặc định; không migrate database dùng chung.

Kiểm chứng cuối: **914 passed, 0 failed, 0 skipped** toàn solution Release (123 ClubReportHub, 85 Admin integration, 706 Backend stabilization), gồm **32 test tài nguyên**. Build `-warnaserror`, format của service/test liên quan và Shared theo CI, kiểm tra EF model/migration của cả 8 service, coverage và `git diff --check` đều đạt. Có test HTTP qua TestServer, chữ ký JWT, nguồn quyền giả lập, persistence SQLite, migration/CAS trên SQL Server thật và chọn route qua ASP.NET/YARP. Test bổ sung đã bắt được endpoint cũ 404 và lỗi PATCH trường bỏ qua trước khi sửa. Không phải nghiệm thu FE/deploy/R2 thật.

Phần chưa hoàn thành được giữ lại: DOCUMENT/MEDIA upload-intent/commit, kiểm tra bytes/MIME/container, scanner/quarantine, private download, expiry/orphan cleanup, public anonymous activity detail và quyền specialist từ governance. Tính điểm/credit/wallet/rewards tiếp tục tạm hoãn theo yêu cầu chủ sản phẩm; thư viện liên kết không sinh điểm hay event trao điểm.
