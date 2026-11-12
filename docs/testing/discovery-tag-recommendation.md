# Kiểm thử tag khám phá và gợi ý

## Phạm vi và dữ liệu

Dùng tài khoản kiểm thử cho Admin, CTSV, quản lý CLB và hai sinh viên ở hai campus. Chuẩn bị một CLB đang tuyển, một CLB đã là thành viên, một hoạt động CLB công khai, một hoạt động nội bộ và một hoạt động do CTSV tổ chức. Ứng viên gợi ý phải đang mở đăng ký, còn chỗ và có ít nhất một buổi hợp lệ. Gắn cùng một tag tự thêm, ví dụ `QA Thiết kế API`, cho hai mục có thể gợi ý để kiểm tra tín hiệu chéo giữa CLB và hoạt động.

## Kiểm tra trước khi phát hành

| Luồng | Ca kiểm thử | Kết quả mong đợi |
| --- | --- | --- |
| Tag mặc định và tag tự thêm | Lưu và mở lại hồ sơ CLB, hoạt động CLB và hoạt động cấp campus. Kiểm tra sau khi duyệt. Thử loại trải nghiệm +1, tag trùng khác hoa thường, quá 10 tag và phiên bản sửa cũ. | Tag đúng được giữ từ hồ sơ đến bản được duyệt. Dữ liệu sai trả 400, phiên bản cũ trả 409. Tag trải nghiệm chỉ mô tả nội dung khám phá. |
| Quyền và hiển thị | Người ngoài CLB thử sửa tag và ghi tương tác với hoạt động nội bộ. CTSV thử sửa hoạt động của campus khác. | Máy chủ từ chối. Kết quả tìm kiếm và gợi ý chỉ chứa mục người dùng có quyền xem. |
| Tìm và lọc | Tìm từ khóa chỉ có trong tag. Chọn tag cùng thể loại CLB, trạng thái hoạt động hoặc đang tuyển. Tải lại và dùng nút Back. | Bộ lọc kết hợp đúng, giữ lựa chọn trong URL và quay về trang đầu khi đổi tiêu chí. |
| Click và view | Mở thẻ một lần. Giữ thẻ hiện trong 4,9 giây, cuộn lại, chuyển tab rồi trở về. Sau đó giữ đủ 5 giây và rời vùng nhìn. Gửi lại cùng mã sự kiện. | Click ghi ngay. View chỉ ghi sau thời gian và tỷ lệ hiển thị yêu cầu. Cuộn hoặc ẩn tab đặt lại bộ đếm. Mỗi lần thẻ xuất hiện đủ điều kiện ghi tối đa một view. Gửi lại cùng mã tạo một bản ghi. |
| Cấu hình Admin | Admin đổi thành 7 giây và 75%, lưu rồi tải lại. CTSV và sinh viên thử gọi API lưu. Tắt view trong khi ClubHub vẫn mở. | Chỉ Admin lưu được. Giá trị mới còn sau tải lại và có nhật ký. API bỏ qua view mới khi đã tắt. Ngưỡng sai trả 400, phiên bản cũ trả 409. |
| Feed và xếp hạng | So sánh trước và sau khi sinh viên tương tác với mục cùng tag. Thử hết chỗ, hết hạn, trùng buổi bắt buộc, campus khác và tài khoản khác. | Sau lần đồng bộ kế tiếp, ứng viên có tag khớp được tăng điểm và có lý do `TAG_INTEREST_MATCH`. Các điều kiện đủ tư cách vẫn được áp dụng. Sinh viên thứ hai có tín hiệu riêng. |
| Dữ liệu cũ và triển khai | Chạy migration trên bản sao trước phát hành. Kiểm tra CLB và hoạt động chưa có tag, Admin chưa từng lưu cấu hình và snapshot từ API phiên bản trước. | Dữ liệu cũ vẫn mở được, tag mặc định được suy ra, cấu hình dùng 5 giây và 50%, engine đọc được snapshot cũ. |

## Lệnh chạy và tiêu chí phát hành

- ClubHub API dùng `dotnet test tests/Backend.StabilizationTests` và `dotnet test tests/AdminService.IntegrationTests`. Chạy workflow **Backend CI** để kiểm tra SQL Server thật và các bộ kiểm thử còn lại.
- ClubHub UI dùng `node --test src/pages/v2/*.test.js src/services/*.test.js tests/*.test.js` và `npm run build`. FPTUX Admin dùng `npm test` và `npm run build`.
- Recommendation engine dùng `python -m unittest discover -s tests -v`. Workflow phát hành chạy thêm các ca MongoDB bằng `MONGODB_TEST_URI`.
- Triển khai engine tương thích trước bằng `rcm_ref` trỏ đúng commit. Sau khi API và migrations sẵn sàng, triển khai ClubHub UI và FPTUX Admin.

## Kiểm thử sau triển khai

1. Xác nhận trang Cloudflare đang phục vụ đúng bản build và API trả 200 ở trang sức khỏe. Kiểm tra endpoint cấu hình ghi nhận yêu cầu đăng nhập.
2. Chạy browser smoke trên URL Cloudflare với dữ liệu kiểm thử được chặn tại trình duyệt. Ở ClubHub, thử tìm và lọc tag, rồi đo click và view theo cấu hình. Ở Admin, thử nhập thông số, lưu, tải lại và mở bằng role CTSV.
3. Với tài khoản kiểm thử trên môi trường đã triển khai, mở một mục thử đủ điều kiện. Đối chiếu tương tác, phiên bản cấu hình, lần đồng bộ kế tiếp và thứ hạng gợi ý. Nếu kiểm tra sửa cấu hình trên production, lưu giá trị ban đầu và hoàn trả ngay sau phép thử.
4. Ghi lại URL, SHA hoặc workflow run, loại tài khoản kiểm thử, kết quả từng ca và lỗi còn lại. Phân biệt kết quả browser với API mô phỏng và xác nhận dữ liệu đã đi qua API, SQL và engine đang chạy.
