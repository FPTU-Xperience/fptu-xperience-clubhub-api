# Báo cáo đối chiếu BM_FIX_AUDIT_REPORT_1 — Review 2

**Ngày đối chiếu:** 28/10/2026, múi giờ Việt Nam.\
**Mục đích:** Gửi nhóm làm tài liệu để sửa checklist và bộ BM theo đúng bằng chứng, yêu cầu môn học và quyết định đã xác nhận.\
**Phạm vi:** Kiểm tra báo cáo `BM_FIX_AUDIT_REPORT_1.md`, các mẫu Review 2, bộ BM đã điền trên GitHub, SRS nháp, phiếu đăng ký và source hiện có. Báo cáo này không phải một lần chạy BM02 chính thức, không xác nhận nghiệm thu và không sửa các biểu mẫu hay mã nguồn.

## 1. Kết luận chính

Báo cáo được đối chiếu **đúng một phần**, nhưng có kết luận sai, có nhận định dựa trên bản cũ và bỏ sót lỗi đáng ưu tiên. Không nên dùng nhãn màu xanh hoặc “>95% confident” làm căn cứ sửa tự động.

Các kết quả quan trọng:

- Đúng: BE hiện dùng Redis Streams cho thông báo; chưa tìm thấy Firebase/FCM trong phạm vi source BE đã kiểm tra. `NOT_HELD` chưa được triển khai trong source này. `BudgetProposal`/`Settlement` là tài chính tiền tệ, không phải ngân sách điểm thô.
- Đúng: Cả năm repo được báo cáo nhắc đến chưa có tag `review-2` tại lần kiểm tra remote ngày 28/10/2026.
- Sai hoặc chưa đủ bằng chứng: kết luận quest chỉ trả `201`; kết luận sáu NFR test bị đếm trùng; phép tính hạn ba ngày làm việc; đề xuất thêm D6; nhận định BM03 bị lệch nhãn/giá trị.
- Đã lỗi thời với bản trên `main`: BM02 không còn năm `[CONFIRM]`; BM01/BM02 đã có PDF; các HTML remake đã bị xóa khỏi repo.
- Bỏ sót: BM01 vượt giới hạn trang; công thức +1 khác bản được chốt; phân công cá nhân trái xác nhận của nhóm; bằng chứng “Fixed” và đầu vào của BM02 còn thiếu tính thuyết phục.

## 2. Những quyết định nhóm đã xác nhận

Các quyết định dưới đây được xác nhận trực tiếp trong phiên trao đổi ngày 28/10/2026; cần đồng bộ vào tài liệu trước khi chốt bản nộp.

| Nội dung | Quyết định dùng để đối chiếu |
| --- | --- |
| Cách tính điểm | Dùng cách tính trong `FPTU_Xperience_Yeu_cau_de_tai_V1_2_Review.docx`; toàn bộ phụ lục A của bản này đã được xác nhận chốt. |
| Tính năng bổ sung | Giữ các tính năng bổ sung mới, gồm Mobile và ngân sách điểm thô; không tự loại chúng chỉ vì chưa có implementation. Nội dung bổ sung phải được hòa giải với cách tính V1.2. |
| Phân công Sinh viên/Quản lý CLB | Thái Hoàng Nam và Nguyễn Kiên Cường. |
| Phân công Admin/xác minh | Nguyễn Hữu Thành và Phạm Thế Anh. |
| Actor CTSV | Tương ứng Người xác minh được phân quyền; quyền xử lý phải gắn với phân công và phạm vi, không chỉ tên vai trò. |
| Giao diện chính | `/v2` dùng API thật. Nếu giữ Mobile là sản phẩm bắt buộc thì cần có thêm bằng chứng nghiệm thu Mobile. |
| Triển khai | Nhóm xác nhận FE và BE đã deploy. Chưa có URL và bằng chứng CI/staging được kiểm chứng trực tiếp trong lần đối chiếu này. |
| Lịch Review 2 | Chưa được cung cấp ngày chính thức. Không coi ngày `21/10/2026` đang điền trong BM là lịch đã xác nhận. |

**Chưa chốt thay nhóm:** Không tự suy ra ai trong mỗi cặp sở hữu workflow nào. Việc phân một owner cụ thể cho từng workflow, quyết định thiết kế và bộ test cần được nhóm xác nhận.

**Lưu ý phiên bản:** Phiếu V1.2_Review nêu trên là file riêng, không được tự đồng nhất với một file khác chỉ vì cũng mang số v1.2 trên repo. Phiếu v1.6, SRS nháp và `final-product-baseline.md` hiện có nội dung khác nhau. Cần có một bản đăng ký hợp nhất theo các quyết định trên; không mặc nhiên coi bất kỳ một bản đang mâu thuẫn nào là bản cuối.

## 3. Nguồn và giới hạn bằng chứng

### 3.1. Phiên bản được kiểm tra

| Nguồn | Phiên bản/phạm vi |
| --- | --- |
| Báo cáo đầu vào | `BM_FIX_AUDIT_REPORT_1.md`, SHA-256: `4aea0e2cfb3b082870207d349a5ed61d12c185c0ca0a4476e3de9db463b0cf4b`. |
| Phiếu tính điểm do nhóm cung cấp | `FPTU_Xperience_Yeu_cau_de_tai_V1_2_Review.docx`, SHA-256: `0e5635cd763f8c2515e9d8917ecd92705cc2d5b48f8a73a085ff37bdc40ce145`. Cần cung cấp đúng file này cho người sửa tài liệu. |
| Repo `capstone-documents` | Commit `bcada53e34971ac7fb13b11a03b0e89d15594872`; bộ DOCX/PDF trong `report/output/review2/`, SRS nháp và phiếu v1.6. |
| Repo `fptu-xperience-doc` | Commit `1afee809dcd2c8371ad5f5bff9e14c5b0baf1583`; baseline và các tài liệu kiến trúc/contract liên quan. |
| BE local | HEAD `b1edcbf1f4aefb95c5d8020ee9f7d159b9c20a62`, **có thay đổi chưa commit**. Các dẫn chứng code bên dưới là source trong working tree đã đọc, không khẳng định toàn bộ thay đổi đó đã có trên GitHub hoặc staging. |
| FE ClubHub local | HEAD `b3e639f1bb1da3812632b1b119a920bf579d2c1e`; dùng cho bối cảnh đối chiếu. |
| Quy định môn học | Bộ mẫu BM01–BM04 và `SWP490_R2_Huong-dan-chuan-bi-Review-2_1 (1).pdf` do nhóm cung cấp. |

### 3.2. Liên kết để người nhận đối chiếu

- [Bộ BM đã điền tại commit được kiểm tra](https://github.com/FPTU-Xperience/capstone-documents/tree/bcada53e34971ac7fb13b11a03b0e89d15594872/report/output/review2).
- [BM01 PDF](https://github.com/FPTU-Xperience/capstone-documents/blob/bcada53e34971ac7fb13b11a03b0e89d15594872/report/output/review2/SWP490_FA26SE224_R2_DesignBrief.pdf).
- [BM02 DOCX](https://github.com/FPTU-Xperience/capstone-documents/blob/bcada53e34971ac7fb13b11a03b0e89d15594872/report/output/review2/SWP490_FA26SE224_R2_SelfReview.docx).
- [BM03 PDF](https://github.com/FPTU-Xperience/capstone-documents/blob/bcada53e34971ac7fb13b11a03b0e89d15594872/report/output/review2/SWP490_FA26SE224_R2_BM03_Record_of_Changes.pdf).
- [BM04 DOCX](https://github.com/FPTU-Xperience/capstone-documents/blob/bcada53e34971ac7fb13b11a03b0e89d15594872/report/output/review2/SWP490_FA26SE224_R2_BM04_Review2_Commitment.docx).
- [SRS nháp](https://github.com/FPTU-Xperience/capstone-documents/blob/bcada53e34971ac7fb13b11a03b0e89d15594872/report/draft/Report3_SRS_Draft_FPTU_Xperience.md).
- [Phiếu v1.6 trên repo](https://github.com/FPTU-Xperience/capstone-documents/blob/bcada53e34971ac7fb13b11a03b0e89d15594872/phieu-dang-ky/FA26SE224_Phieu_dang_ky_v1_6_VI.docx).
- [Bộ mẫu và hướng dẫn Review 2 trên repo](https://github.com/FPTU-Xperience/capstone-documents/tree/bcada53e34971ac7fb13b11a03b0e89d15594872/report/template/review2).
- [Baseline của repo tài liệu](https://github.com/FPTU-Xperience/fptu-xperience-doc/blob/1afee809dcd2c8371ad5f5bff9e14c5b0baf1583/documents/final-product-baseline.md).

### 3.3. Cách đọc kết quả

- **Đúng:** Có bằng chứng trực tiếp trong phạm vi đã kiểm tra.
- **Đúng một phần:** Có vấn đề thật nhưng kết luận hoặc cách sửa cần điều kiện bổ sung.
- **Cần sửa:** Có bằng chứng phản bác kết luận/cách sửa trong báo cáo.
- **Đã lỗi thời:** Không còn khớp bộ tài liệu hiện có ở commit được kiểm tra; không khẳng định bản cũ chưa từng có lỗi đó.
- **Chưa đủ bằng chứng:** Thiếu bản nguồn, kết quả chạy hoặc xác nhận để kết luận.

Không chạy lại build, test, load test hay thao tác nghiệp vụ trên staging trong lần đối chiếu này. Sự tồn tại của test trong source không được gọi là một kết quả test vừa chạy đạt.

## 4. Đối chiếu từng nhóm nhận định trong báo cáo

### 4.1. BM01 — Design Brief

| Mã trong báo cáo | Kết quả | Nội dung cần giữ hoặc điều chỉnh |
| --- | --- | --- |
| BM01-1 | Đúng | Code có `Cancelled`/`CANCELLED`; chưa thấy `NOT_HELD`. Không viết cả hai đều đã được dùng trong code. Nếu giữ `NOT_HELD` theo tính năng bổ sung, phải ghi là trạng thái mục tiêu chưa triển khai. |
| BM01-2 | Đúng một phần | Không tìm thấy `V2_create_tables.sql` trong source BE đã kiểm tra. `EvidenceUrl` có trên `SelfDeclaration`, không tự chứng minh entity `Contribution` đã khớp ERD. Cần map đúng entity và mô hình minh chứng trước khi đề xuất thêm cột. |
| BM01-3 | Đúng | Chưa có tag `review-2`. Ghi “sẽ tạo” chỉ mô tả tiến độ, không thay thế điều kiện phải có tag thật khi nộp/chạy review chính thức. |
| BM01-4 | Cần kiểm tra quyền và liên kết cụ thể | Không bắt buộc repo phải public nếu reviewer có quyền đọc phù hợp. Nên dẫn từng repo và đúng revision/tag; link trang tổ chức chưa xác định được snapshot của từng thành phần. |
| BM01-5 | Đúng một phần | Các con số 100 client, p95 ≤ 2 giây, 10.000 bản ghi và 5 lần × 5 phút là cam kết trong BM, chưa phải kết quả đo đã xác minh. Không tự giảm ngưỡng chỉ vì chưa đo được; phải thống nhất và cập nhật các tài liệu liên quan. |

**Vấn đề báo cáo bỏ sót:** BM01 PDF có **10 trang**; mục 8 vẫn ở **trang 7**, phụ lục A bắt đầu ở cuối trang 7 và phần A1 ở trang 8. Mẫu giới hạn mục 0–8 tối đa **4 trang A4**, không tính phụ lục A. Cần rút gọn và dàn trang lại phần chính, không chỉ đổi tên file.

### 4.2. BM02 — Self-Review Report

| Mã trong báo cáo | Kết quả | Nội dung cần giữ hoặc điều chỉnh |
| --- | --- | --- |
| BM02-1 | Đã lỗi thời | BM02 DOCX/PDF tại commit được kiểm tra có **0 `[CONFIRM]`**, không phải năm chỗ. Tuy nhiên các dẫn chứng được điền vẫn cần kiểm tra tính đúng đắn. |
| BM02-2 | Đúng trong phạm vi BE đã đọc | Không tìm thấy Firebase/FCM trong source BE; notification có Redis Streams. Sơ đồ cần phản ánh thành phần thật. `KpiGrpcService` là dịch vụ nội bộ của dự án, không nên mô tả thành nhà cung cấp bên thứ ba. |
| BM02-3 | Đúng về giới hạn kết luận | Có bằng chứng trạng thái trong code, nhưng chưa có Report 4/SDD đầy đủ để xác nhận thiếu trạng thái ở đúng sơ đồ/mục 5.1 được dẫn. Tài liệu kiến trúc rời trong repo doc không tự chứng minh mục SDD đó tồn tại. |
| BM02-4 | Cần sửa | `JoinQuestAsync` có cả nhánh trả 200 và nhánh trả 201. Không kết luận chỉ từ `.Produces(201)`, và không gộp các endpoint đọc/cập nhật/hoàn thành với endpoint tạo mới. Route `/api/v1/quests/claim` không được map trong `QuestEndpoints` đã kiểm tra. |
| BM02-5 | Cần sửa cách xử lý phạm vi | Không tự loại toàn bộ gợi ý/nhiệm vụ theo SRS nháp cũ. Phiếu V1.2_Review và phiếu v1.6 đều có nội dung nhiệm vụ. Phải phân biệt nhiệm vụ/gợi ý bằng quy tắc với khuyến nghị nâng cao bằng AI/ML; đồng bộ lại phiếu, SRS và BM. |
| BM02-6 | Đúng | `review-2 / review-2` không đủ làm bằng chứng tag/commit trong tình trạng tag chưa có. Cần chỉ rõ repo, tag thật và commit SHA của từng đầu vào. |

**Đính chính chi tiết HTTP:**

| Route trong BE | Trường hợp | Kết quả từ code |
| --- | --- | --- |
| `POST /api/v1/quests/{id}/join` và alias `/api/quests/{id}/join` | Tạo lượt tham gia mới | `201`, `QuestEndpoints.cs:493`. |
| Cùng route join | Đã có lượt tham gia, đi vào nhánh `existing` | `200`, `QuestEndpoints.cs:478`. |
| `POST /api/v1/quests/{id}/complete` | Hoàn thành sau kiểm tra điều kiện | Có nhánh thành công `Results.Ok`, `QuestEndpoints.cs:545`; không được dùng làm bằng chứng rằng endpoint tạo mới sai mã. |
| `/api/v1/quests/claim` | Route được SR-06 viện dẫn | Chưa xác định được handler tương ứng trong `QuestEndpoints`; cần sửa định danh đầu vào của finding trước khi bàn chuyện đổi mã trả về. |

**Bằng chứng “Fixed” chưa đủ:** Commit `5837e8eceeeb08fa9bb156b549f2acd13b0b2e2f` có thật trong `capstone-documents`. Danh sách file thay đổi chủ yếu là BM03/BM04, script và bản trích xuất, không có bản SRS được sửa để chứng minh NFR đã hết thiếu sót. SRS nháp hiện vẫn đặt chỉ tiêu hiệu năng tại `NFR-09` dưới dạng cần chốt. Vì vậy, riêng commit này chưa chứng minh được trạng thái Fixed của SR-02 về SRS.

[Commit 5837e8e và danh sách thay đổi](https://github.com/FPTU-Xperience/capstone-documents/commit/5837e8eceeeb08fa9bb156b549f2acd13b0b2e2f).

**Quy tắc làm lại BM02:** Mẫu yêu cầu giữ nguyên kết quả AI và chỉ thêm cột Team response. Nếu đầu vào/phiên bản cũ không đúng hoặc finding sai, phải phản hồi bằng bằng chứng; nếu chạy lại thì lưu rõ một lần chạy mới với đúng bộ đầu vào và revision. Không biên tập Finding/Evidence/Top 5 cũ rồi trình bày thành kết quả AI nguyên bản. Việc thiếu SDD chỉ cho phép ghi “không kiểm chứng được”, không cho phép suy đoán nội dung sơ đồ mục tiêu.

### 4.3. BM03 — Record of Changes

| Mã trong báo cáo | Kết quả | Nội dung cần giữ hoặc điều chỉnh |
| --- | --- | --- |
| BM03-1 | Đúng một phần | Khi nộp, BM03 phải đi cùng phiếu đăng ký trong PDF Registration. Số phiên bản `1.7` do nhóm lựa chọn/đang khai trong BM, không phải số phiên bản bắt buộc do hướng dẫn môn học quy định. Nếu giữ khai v1.7 thì cần có đúng phiếu v1.7 tương ứng. |
| BM03-2 | Cần sửa | Đã kiểm tra bảng DOCX và render trang 1 PDF hiện tại: `Supervisor`, `Current registration version = 1.6`, `Proposed version = 1.7` ghép đúng nhãn và giá trị. Không có đủ căn cứ giữ kết luận hai cell bị lệch đối với bản này. |
| BM03-3 | Đúng | Phải đối chiếu số mục với đúng phiếu sẽ nộp. Cột Current phải mô tả đúng bản hiện hành, Proposed phải mô tả đúng bản đề xuất; không chỉ kiểm tra tên section. |

Đặc biệt, các nội dung raw budget, Mobile và công thức trong phiếu v1.6 có thể đã tồn tại trước bản đề xuất. Không được ghi chúng đều là “mới thêm ở v1.7” nếu cột Current không phản ánh đúng v1.6 thật.

### 4.4. BM04 — Review 2 Commitment

| Mã trong báo cáo | Kết quả | Nội dung cần giữ hoặc điều chỉnh |
| --- | --- | --- |
| BM04-1 | Đúng về lỗi, cần sửa cách khắc phục | D-ID/nội dung/owner giữa BM01 và BM04 đang lệch. Tuy nhiên đề xuất “thêm D6” không khớp yêu cầu BM01 có đúng năm quyết định. Phải phân công lại theo nhóm đã xác nhận rồi thống nhất D1–D5. |
| BM04-2 | Đúng | Chưa có xác nhận kết quả Review 2 thì không được tự khai `Approved = Yes` hoặc kết quả xác minh của giảng viên. Có thể chuẩn bị bản nháp cam kết, để phần quyết định/xác nhận theo đúng thực tế. |
| BM04-3 | Đúng trong phạm vi BE | Không yêu cầu ghi FCM thành thành phần đã triển khai khi chưa có source chứng minh. Redis Streams và gRPC cần mô tả đúng vai trò/phạm vi. |
| BM04-4 | Đúng về phân biệt domain | `BudgetProposal.RequestedAmount`, `ApprovedAmount`, `Settlement.TotalSpent`, `ReceiptUrl` là dấu hiệu của domain tiền tệ. Không dùng chúng làm minh chứng đã hoàn thành ngân sách điểm thô. Danh sách module thay thế vẫn cần map với source và người thực sự phụ trách. |
| BM04-5 | Cần sửa | Chưa có bằng chứng đếm trùng. Tổng theo workflow và theo thành viên đều bằng 70; tự động đều bằng 42. Không giảm Nam từ 19 xuống 13 chỉ vì sáu NFR xuất hiện ở hai bảng tổng hợp khác nhau. |
| BM04-6 | Đúng về khoảng trống, cần sửa cách xử lý | Chưa có bằng chứng repo Mobile trong bộ nguồn được kiểm tra. Tuy nhiên nhóm đã xác nhận giữ tính năng mới: phải ghi Mobile chưa hoàn thành và có kế hoạch thực tế; không dùng demo web làm bằng chứng đã hoàn thành Mobile. |
| BM04-7 | Cần xác nhận thực tế | Tên reviewer, ngày Review và ngày ký chưa được cung cấp. Không suy ra lịch Review chỉ từ ngày đang điền trong bản nháp, cũng không đồng nhất ngày Review với ngày hoàn tất/ký cam kết. |
| BM04-8 | Cần sửa | Nếu Review là 21/10/2026 và tính thứ Hai–thứ Sáu, ba ngày làm việc tiếp theo là 22/10, 23/10, 26/10. Không phải 28–30/10. Nhóm chưa xác nhận ngày Review thực nên chưa thể chốt hạn nộp bằng ngày giả định này. |
| BM04-9 | Đúng một phần | Phân biệt module mục tiêu với module đã làm. File hợp đồng DTO hoặc tên `StudentExperienceContracts.cs` không đủ chứng minh có entity `StudentProfile`; cần dẫn model/DbContext/endpoint phù hợp. |

**Kiểm tra lại phép cộng trong BM04 hiện tại:**

| Cách tổng hợp | Test cam kết | Tự động trong CI được cam kết |
| --- | --- | --- |
| Theo workflow và NFR | `29 + 12 + 10 + 13 + 6 = 70` | `16 + 7 + 6 + 7 + 6 = 42` |
| Theo thành viên | `29 + 12 + 10 + 19 = 70` | `16 + 7 + 6 + 13 = 42` |
| Tỷ lệ | 70 test | `42 / 70 = 60%` |

Hai bảng phân loại cùng một tập test theo hai chiều; **không cộng tổng của hai bảng với nhau**. Cần lập danh sách ID test duy nhất, mỗi test tính cho đúng một thành viên và có ánh xạ nguồn yêu cầu. Dòng NFR “All / Cross-cutting” nên làm rõ người viết test để khớp bảng cá nhân. Số học đúng không chứng minh 70 test đã tồn tại, đã đạt hay đã đủ độ bao phủ; số 12 chuyển trạng thái phải được đối chiếu với các state machine thực tế.

### 4.5. Các mục xuyên suốt

| Mã trong báo cáo | Kết quả | Nội dung cần giữ hoặc điều chỉnh |
| --- | --- | --- |
| ALL-1 | Đúng một phần, có thông tin cũ | BM01/BM02 hiện đã có PDF mang đuôi `R2_DesignBrief.pdf` và `R2_SelfReview.pdf`. Không giữ nhận định cả hai chưa export. Tên PDF BM04 hiện còn khác mẫu `R2_Commitment.pdf`; kiểm tra lại tên bộ nộp cuối. |
| ALL-2 | Đúng, cần đổi thứ tự thực hiện | Cả năm remote repo được nêu chưa có tag `review-2`. Tuy nhiên không nên coi tạo tag là việc đầu tiên khi baseline và hồ sơ còn lệch; phải chốt snapshot đã kiểm tra, gắn tag đúng commit, rồi dùng snapshot đó cho lần self-review chính thức. |
| ALL-3 | Đúng một phần | Chưa tìm thấy phiếu v1.7 trong cây repo tại commit kiểm tra. Phải có phiếu hợp nhất khớp phiên bản BM đang khai, nhưng hướng dẫn không bắt buộc mọi nhóm đặt số 1.7. |
| ALL-4 | Đúng | SRS nháp dùng nhóm NFR tại mục 4, gồm NFR-01 đến NFR-10; không trùng cách dẫn `SRS §3.2 / NFR-PERF-01`. Cần map ID/section thật, không sửa bằng cách đổi tên cho giống một finding cũ. |
| ALL-5 | Đúng trong phạm vi BE đã đọc | Không giữ FCM là thành phần đã có nếu không có bằng chứng. Đồng bộ cả BM01 Known Gaps, BM02, BM04 Required Fixes và tài liệu kiến trúc liên quan. |
| ALL-6 | Đúng về thiếu đầu vào | Chưa tìm thấy Report 4/SDD đầy đủ tương ứng các số mục đang dẫn trong bộ tài liệu được kiểm tra. Có một số tài liệu kiến trúc ở repo doc, nhưng chưa đủ để xác nhận các finding cụ thể đang ghi. Ghi “target SDD” không thay thế việc phải có đúng đầu vào cho BM02 chính thức. |

## 5. Các vấn đề bổ sung cần xử lý trước khi chốt bộ BM

### 5.1. Đồng bộ công thức +1 về V1.2

BM01 D5 và baseline cũ của repo doc đang dùng:

```text
W = 1 + 0.30 × min(S7 / T7, 1)
```

Theo phụ lục A của phiếu V1.2_Review đã được nhóm xác nhận, cần giữ:

```text
P  = B × min(R × Q, 8) × (1 + K)
Sj = tổng điểm phân bổ còn hiệu lực của trụ cột j
Xj = 100 × (1 − exp(−Sj / tau_j))
D  = trung bình sáu Xj của sáu trục radar
J  = độ đều Pielou trên đúng sáu Xj đó
M  = 1 + 0.30 × X_plus1 / 100
ERI = D × (0.5 + 0.5 × J) × M
```

Với `S_plus1 = tau_plus1 = 1000`, cách bão hòa cho `M ≈ 1.18963617`; cách tuyến tính với `S7 = T7 = 1000` cho `W = 1.30`. Đây là khác biệt kết quả, không chỉ khác tên biến.

Cần giữ các quy tắc biên của V1.2: +1 tách khỏi sáu trục và D/J; sáu trục bằng 0 thì ERI bằng 0; tính với số chưa làm tròn; bậc Dẫn dắt cần đóng góp lãnh đạo hợp lệ. Các quy tắc vòng đời/kiểm chứng mới không được âm thầm thay công thức điểm. Nếu ngân sách điểm thô, hệ số chuyển đổi hoặc action `basePoints` tạo cách tính khác, phải hòa giải rõ với V1.2 trước khi ghi là “đã chốt”.

### 5.2. Phân công trong BM đang trái xác nhận của nhóm

BM hiện giao Nguyễn Kiên Cường WF-03 rollover/Admin và Phạm Thế Anh WF-02 Sinh viên/Mobile. Cách giao này không khớp xác nhận Nam–Cường phụ trách Sinh viên/CLB, Thành–Anh phụ trách Admin/xác minh.

Cần đồng bộ các mục sau sau khi nhóm chốt owner cá nhân:

- BM01: bảng Ownership, owner các quyết định thiết kế, risks, gaps và phụ lục workflow/demo.
- BM04: owner feature/NFR, workflow, presenter, test, fixes và cam kết từng thành viên.
- SRS/SDD/Tracking/Report 5: ánh xạ yêu cầu, module, đầu việc và bằng chứng tương ứng.

Không chỉ đổi tên người ở một bảng rồi giữ các bảng còn lại như cũ. Không tự phân ai trong mỗi cặp làm một workflow cụ thể khi chưa được xác nhận.

### 5.3. Không suy ra thiếu test chỉ từ thiếu một tên file

Source BE có `tests/Backend.StabilizationTests/ActivityActionContractTests.cs`, trong đó có:

- `Application_DraftSubmitRevisionReviewPreservesHistoryAndCreatesOneUnpublishedActivityWithoutPoints()`.
- `Application_CannotReadOtherClubReviewOwnApplicationOrReplayAfterRevocation()`.
- `Staffing_CompletionVerificationRequiresIndependentCurrentCapabilityAndNeverAwardsPoints()`.

Các test này là bằng chứng có kiểm thử một số bước duyệt đơn, phân quyền và vận hành chưa ghi điểm. Chúng **không chứng minh** toàn luồng hai cổng duyệt, ngân sách và posting đã hoàn thành. Finding cần nêu đúng bước/điều kiện còn thiếu, không kết luận không có test duyệt chỉ vì thiếu file tên `ActivityApprovalWorkflowTests.cs`.

### 5.4. Không thêm cột minh chứng khi chưa map đúng mô hình

Dẫn chứng BE:

- `src/Services/AdminService/Data/Migrations/20261006150939_AddSelfDeclarations.cs:27`: `SelfDeclarations.EvidenceUrl`.
- `src/Services/AdminService/Data/AdminDbContext.cs:101`: cấu hình trường `SelfDeclaration.EvidenceUrl`.
- `src/Services/AdminService/Data/VerifiedExperienceEntities.cs:19` và `:39`: `VerifiedContribution` lưu `EvidenceRefsJson`.
- Cùng file có `PrivateContributionEvidence`, thể hiện mô hình minh chứng riêng.

Do đó phải xác định ERD đang nói về `SelfDeclaration`, `VerifiedContribution` hay một entity khái niệm khác. Không thêm `Contribution.evidence_url` chỉ để khớp một finding chưa có ERD nguồn, đặc biệt khi mô hình thực tế dùng nhiều tệp minh chứng và kiểm soát quyền truy cập.

### 5.5. Lịch thực tế và trạng thái tài liệu

Ngày Review, ngày lập tài liệu, ngày hoàn tất cam kết và ngày ký là các trường có ý nghĩa khác nhau. Chưa có lịch Review thực được xác nhận thì không tự gán ngày hoàn tất 30/10/2026, không tự ghi reviewer đã đồng ý và không tự đổi các due date để làm cho biểu mẫu trông hợp lệ.

Quy định BM04 là hoàn tất trong ba ngày làm việc sau Review 2 và được cả hai reviewer cùng giảng viên hướng dẫn xác nhận. Trước buổi Review có thể chuẩn bị đề xuất, nhưng phải thể hiện đúng trạng thái chưa được phê duyệt.

## 6. Thứ tự sửa đề xuất cho nhóm tài liệu

1. Hợp nhất một phiếu đăng ký: giữ công thức V1.2, giữ tính năng bổ sung đã xác nhận và ghi rõ phần chính sách bổ sung còn cần thống nhất; chọn đúng số phiên bản sẽ dùng.
2. Chốt owner từng workflow/cam kết theo hai nhóm phụ trách đã xác nhận. Đồng bộ toàn bộ bảng liên quan.
3. Đồng bộ SRS, thiết kế và code: ID yêu cầu, trạng thái hiện có/mục tiêu, công thức, actor, dữ liệu minh chứng, ngân sách và cách ghi nhận điểm.
4. Rút BM01 về giới hạn bốn trang cho mục 0–8; giữ phần chi tiết phù hợp ở phụ lục/Report 3–5. Kiểm tra lại PDF sau dàn trang.
5. Sửa checklist audit theo các đính chính ở báo cáo này: HTTP quest, phép cộng test, ngày làm việc, D1–D5, số placeholder và bố cục BM03. Giữ dấu vết phiên bản thay vì ghi đè kết quả AI gốc.
6. Lập danh sách test ID và minh chứng CI/staging thực tế. Phân biệt số cam kết với số đã viết, đã chạy và đã đạt. Hoàn thiện các đầu vào thiết kế còn thiếu.
7. Chốt đúng snapshot để tạo tag `review-2`, dẫn commit/repo cụ thể, thực hiện lần BM02 đúng prompt trên bộ đầu vào đó. Phản hồi từng finding bằng bằng chứng hoặc owner/due date thực tế.
8. Xuất và kiểm tra bộ PDF nộp cuối; gộp BM03 với phiếu đăng ký. Hoàn tất phần xác nhận của BM04 theo kết quả Review thực tế.

## 7. Danh sách xác nhận khi bàn giao lại tài liệu

- [ ] Phiếu đăng ký hợp nhất giữ cách tính V1.2 và mô tả đúng phần bổ sung; không còn hai baseline mâu thuẫn.
- [ ] Nam–Cường và Thành–Anh được phân đúng nhóm nghiệp vụ; owner cá nhân được nhóm xác nhận.
- [ ] BM01 mục 0–8 không quá bốn trang; có đúng năm quyết định thiết kế và mỗi quyết định một owner.
- [ ] BM01/BM04 dùng cùng D-ID, feature, workflow, người trình bày và cam kết.
- [ ] BM02 dùng đúng đầu vào, revision và kết quả AI nguyên bản của một lần chạy xác định được.
- [ ] Finding không còn dẫn FCM, file SQL hoặc endpoint không có căn cứ; mục không kiểm chứng được được ghi đúng giới hạn.
- [ ] Trạng thái Fixed có thay đổi nguồn tương ứng và kiểm chứng phù hợp, không chỉ một commit có thật.
- [ ] Bộ 70 test, nếu tiếp tục chọn mức này, có ID không trùng, phân công cá nhân và số tự động khớp; mức bao phủ được đối chiếu với yêu cầu và trạng thái thực tế.
- [ ] Mobile được ghi đúng mức hoàn thành và có cam kết nghiệm thu riêng nếu tiếp tục là thành phần bắt buộc.
- [ ] Ngày Review, hạn hoàn tất, reviewer và kết quả phê duyệt được điền theo xác nhận thực tế.
- [ ] Có đúng phiếu đăng ký được BM dẫn; BM03 đi cùng phiếu, các PDF có tên và nội dung đúng bản nộp.
- [ ] Tag, CI và staging có bằng chứng tương ứng snapshot nộp; việc triển khai không chỉ được suy ra từ sự tồn tại của source hoặc cấu hình.

**Phạm vi của tài liệu này:** Kết quả đối chiếu tại các phiên bản đã nêu, dùng để sửa báo cáo audit và chuẩn bị đầu vào Review 2. Không thay thế quyết định của giảng viên, biên bản Review hay bằng chứng nghiệm thu của hệ thống.
