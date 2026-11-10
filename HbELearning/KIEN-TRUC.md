# HbELearning — Clean Architecture

Giải pháp gồm **4 project** chạy thật, xếp từ trong ra ngoài (cộng một project kiểm thử, xem cuối mục này). Mũi tên là chiều được phép phụ thuộc:

```
Domain  <--  Application  <--  Infrastructure
                  ^                  ^
                  |                  |
                  +------- API ------+   (API chỉ chạm Infrastructure trong Program.cs)
```

| Project | Được tham chiếu | Gói NuGet |
|---|---|---|
| `HbELearning.Domain` | **không gì cả** | **không có** |
| `HbELearning.Application` | Domain | EF Core (phần trừu tượng) + `Microsoft.Extensions.*.Abstractions` — **không có ASP.NET Core** |
| `HbELearning.Infrastructure` | Application | EF Core + provider, ClosedXML, OpenXml, PdfPig, QRCoder, BCrypt, JWT |
| `HbELearning.API` | Application, Infrastructure | JwtBearer, Swashbuckle, OpenApi |
| `HbELearning.Tests` | Application, Infrastructure | xUnit — chỉ để kiểm thử, **không** được tham chiếu từ project nào |

Các quy tắc trên **do trình biên dịch cưỡng chế**, không chỉ là quy ước:
- `Application.csproj` không tham chiếu Infrastructure lẫn framework ASP.NET → gõ `AppDbContext`, `IFormFile`, `HttpContext` trong Application sẽ không biên dịch được.
- `API/GlobalUsings.cs` không mở namespace nào của Infrastructure; chỉ `Program.cs` tự `using` → gõ `AppDbContext` trong một Controller sẽ không biên dịch được.

**Kiểm thử (`Tests/`).** Kiểm thử ở **tầng Application**: gọi các dịch vụ qua đúng cổng mà API dùng (`IOnlineClassRoomService`, `IClassRoomSessionService`…), chạy trên SQLite trong bộ nhớ với DI thật của `AddApplication()`; chỉ những cổng nhìn ra ngoài (người dùng hiện tại, thư mục, xử lý media, thời gian) là bản giả. Không kiểm thử Controller/Hub (chỉ là lớp truyền tải) và không mock DbContext. Chạy: `dotnet test Tests/HbELearning.Tests.csproj`. Hiện mới phủ phòng học online.

## Luồng một yêu cầu

```
HTTP ─► Controller ─► I<X>Service (Application/Features) ─► Result ─► Controller.ToActionResult ─► HTTP
                            │
                            ├─► IAppDbContext            (Infrastructure: AppDbContext)
                            ├─► thực thể Domain           (quy tắc chuyển trạng thái)
                            └─► cổng Common/Interfaces    (Infrastructure hoặc API hiện thực)
```

Controller chỉ còn một dòng mỗi action: `=> ToActionResult(await _service.X(...))`.

## Cái gì nằm ở đâu

**`Domain/`** — không biết gì về database, HTTP hay thư viện ngoài.
- `Entities/` — thực thể. Các thực thể có vòng đời rõ mang **hành vi** của chính mình:
  `Course` (chuỗi duyệt nội dung), `Enrollment` (tính tiến độ, hoàn thành), `LessonProgress`,
  `ShopProduct` (kho), `ShopRedemption` (chốt lượt đổi), `OnlineClassRoom` (bắt đầu/kết thúc/hủy),
  `StudyReminder` (nhảy mốc lặp).
- `Enums/`, `Constants/` (`RoleName`, `FunctionKey`, `AppClaimType`), `Common/` (quy tắc thuần như `ReminderSchedule`, `PhoneHelper`, `VectorHelper`).

**`Application/`** — nghiệp vụ.
- `Features/<ChứcNăng>/` — **ca sử dụng** mà API gọi: `I<X>Service` + `<X>Service`, mỗi chức năng một thư mục (thường mỗi Controller một thư mục; bảng ở mục *Bản đồ trang*).
- `Services/` — nghiệp vụ **dùng chung** giữa nhiều chức năng: `LearningService`, `RankingEngine`, `NotificationDispatcher`, `CertificateIssuer`, `DataImportService`, `DocumentProcessingService`, `CourseKnowledgeService`, `StudyReminderDispatcher`.
- `Common/Interfaces/` — **chỉ các cổng ra bên ngoài** (20 cổng), mỗi cổng được hiện thực ở Infrastructure hoặc API. VD `IAppDbContext`, `ICurrentUser`, `IFileService`, `IPasswordHasher`, `IEventStream`, `IChatNotifier`.
- `Common/Results/Result.cs` — kết quả ca sử dụng, mô tả bằng nghĩa nghiệp vụ (`Invalid`, `NotFound`, `Forbidden`…), không bằng mã HTTP.
- `Common/Models/` — `FileUpload` (thay `IFormFile`), `ImportRow`, `ImportTemplate`…
- `DTOs/` — hợp đồng dữ liệu vào/ra.

**`Infrastructure/`** — mỗi lớp là phần cắm vào cho đúng một cổng.
- `Persistence/` — `AppDbContext`, `DbProviderSetup`, `Seeding/`, `Initialization/DatabaseInitializer`.
- `Identity/` (JWT, BCrypt), `Ai/` (Ollama), `Documents/` (bóc chữ PDF/Word, Word→HTML), `Import/` (đọc Excel/Word/PDF, ghi file mẫu), `Certificates/` (ảnh QR), `Notifications/` (SMTP, eSMS, Zalo), `Files/`, `BackgroundJobs/` (chỉ vòng hẹn giờ — nghiệp vụ nằm ở Application).

**`API/`** — `Controllers/<Trang>/` (gom theo trang giao diện, namespace `HbELearning.API.Controllers.<Trang>`; `BaseApiController` nằm ở gốc), `Hubs/`, `Authorization/`, `Extensions/`, `wwwroot/`, `Program.cs`, và `Services/` gồm các hiện thực cổng thuộc về tầng web: `CurrentUser` (claim JWT), `ServerSentEventStream` (SSE), `SignalRChatNotifier`. Tên thư mục Controller để số nhiều (`Courses`, `Users`, `Roles`…) cho khỏi trùng tên thực thể (`Course`, `User`, `Role`) — trùng thì tên ngắn trong namespace đó trỏ vào namespace thay vì vào thực thể.

**`API/wwwroot/`** — frontend tĩnh, HTML/CSS/JS thuần, không có bước build.
- `pages/<trang>/` — **mỗi mục trên thanh bên là một thư mục**, chứa `<trang>.html` (khung), `<trang>.css` (giao diện riêng), `<trang>.js` (mã trang). Trang con mở từ trang đó nằm chung thư mục: `course/course-detail.*`, `online-class/classroom.*`, `certificate/verify.*`. Luồng học của học viên (trình phát SCORM và các SCO) ở `pages/learning/`; trang đăng nhập ở `pages/login/`; CSS/JS của trang chủ ở `pages/landing/`.
- `index.html`, `sw.js`, `manifest.webmanifest` — bắt buộc ở gốc (trang mặc định của `/`, phạm vi của service worker).
- `js/` — chỉ chứa thứ **dùng chung**: vỏ ứng dụng nạp ở mọi trang (`api.js`, `app.js`, `language.js`, `theme.js`, `password.js`, `head.js`, `emoji.js`, `pwa.js`), `widgets/` (chat, feedback khóa học, trình phát video có câu hỏi — dùng ở nhiều trang), `sim/` (mô phỏng 3D — dùng ở `course-detail`, `sim-library`, `simulation`), `lib/` (thư viện ngoài).
- `css/` — `style.css` (giao diện chung của mọi trang), `password.css`, bộ biểu tượng `phosphor/`, `uicons/`.
- Quy tắc đặt tệp: chỉ một trang dùng → để trong `pages/<trang>/`; từ hai thư mục trang trở lên → `js/widgets/` hoặc `js/sim/`.
- Đường dẫn trong mã luôn là đường dẫn tuyệt đối (`/pages/course/course.html`, `/js/api.js`) — địa chỉ trên trình duyệt chính là vị trí tệp. Địa chỉ kiểu cũ (`/pages/x.html`, `/login.html`) được `API/Extensions/LegacyPageRedirectExtensions.cs` chuyển hướng 302 vì vẫn còn trong mã QR đã in, dấu trang và đường dẫn SCO lưu trong database — không được gỡ bảng đó.
- Sửa tệp trong `pages/` thấy ngay ở lần tải sau (service worker đi mạng trước cho `/pages/`); sửa `js/`, `css/` dùng chung thì nhớ tăng `VERSION` trong `sw.js`.

## Bản đồ trang

| Trang (thanh bên) | Thư mục `wwwroot/pages/` | `API/Controllers/` | `Application/Features/` |
|---|---|---|---|
| Tổng quan | `dashboard/` | `Dashboard/` | `Dashboard` |
| Khóa học của tôi | `my-course/` | `MyCourses/` (Enrollment, StudyReminder, Feedback) | `Enrollments`, `StudyReminders`, `Feedbacks` |
| *(học bài)* trình phát SCORM, SCO bài học/bài kiểm tra, mô phỏng | `learning/` | `Learning/` (Scorm, Progress, LessonNote) | `Scorm`, `Progress`, `LessonNotes` |
| HB Minerva | `ai-tutor/` | `AiTutor/` | `AiTutor` |
| Chứng nhận (+ trang xác thực QR công khai) | `certificate/` | `Certificates/` | `Certificates`, `ExternalCertificates` |
| Xếp hạng | `ranking/` | `Ranking/` | `Ranking` |
| Trung tâm điểm thưởng | `shop/` | `Shop/` | `Shop` |
| Phòng học online (+ phòng học, xem lại) | `online-class/` | `OnlineClasses/` (+ `Hubs/ClassRoomHub`) | `OnlineClassRooms` |
| Quản lý khóa học (+ soạn khóa học) | `course/` | `Courses/` (Course, Lesson, Quiz, LearningPath, Import, Upload, Scenario, VideoLearning, Ai) | `Courses`, `Lessons`, `Quizzes`, `LearningPaths`, `Import`, `Uploads`, `Scenarios`, `VideoLearnings`, `Ai` |
| Luồng cấp duyệt | `approval-workflow/` | `ApprovalWorkflows/` | `ApprovalWorkflows`, `ApprovalDelegates` |
| Thư viện 3D | `sim-library/` | `SimLibrary/` | `SimAssets` |
| Báo cáo | `report/` | `Reports/` | `Reports` |
| Người dùng | `user/` | `Users/` (User, HrSync) | `Users`, `HrSync` |
| Phòng ban & Vị trí | `organization/` | `Organization/` | `Departments`, `Positions` |
| Vai trò & Phân quyền | `role/` | `Roles/` | `Roles` |
| Dịch ngôn ngữ | `translation/` | `Translations/` | `Translations` |
| Tài liệu AI | `ai-document/` | `AiDocuments/` (AiDocument, AiAnswerMemory, AiQuickReply) | `AiDocuments`, `AiAnswerMemories`, `AiQuickReplies` |
| Thông tin tài khoản | `account/` | dùng `Auth/`, `Certificates/`, `MyCourses/` | — |
| Đăng nhập | `login/` | `Auth/` | `Auth` |
| Thanh trên của mọi trang (chat, thông báo, tìm kiếm) | `js/app.js`, `js/widgets/chat.js` | `Shared/` (+ `Hubs/ChatHub`) | `Chat`, `Notifications`, `Search` |

Kiểm thử trong `Tests/` chia thư mục theo `Application/Features/`.

## Thêm một tính năng thì sửa ở đâu

1. Thực thể mới → `Domain/Entities/`; khai báo `DbSet` ở **cả** `AppDbContext` **và** `IAppDbContext`. Quy tắc chuyển trạng thái → phương thức trên thực thể.
2. Ca sử dụng mới → `Application/Features/<ChứcNăng>/`, trả về `Result`; đăng ký trong `Application/DependencyInjection.cs`.
3. Cần gọi ra ngoài (gửi thư, gọi API, đọc tệp, đẩy thời gian thực) → khai báo cổng ở `Application/Common/Interfaces/`, hiện thực ở `Infrastructure/` (hoặc `API/Services/` nếu là việc của tầng web), đăng ký DI tương ứng.
4. Điểm cuối HTTP → `API/Controllers/<Trang>/`, thân action chỉ gọi service và `ToActionResult`.
5. Trang giao diện mới → thư mục `API/wwwroot/pages/<trang>/` với `<trang>.html` + `.css` + `.js` (chép khung `<head>`/thứ tự `<script>` dùng chung từ một trang có sẵn), thêm mục vào `MENU` trong `js/app.js`, thêm một dòng vào *Bản đồ trang* ở trên.

`Program.cs` chỉ còn phần lắp ráp; đừng thêm logic vào đó.

## Những gì cố ý giữ nguyên

- **Application dùng EF Core qua `IAppDbContext`** (có `DbSet<T>`), không viết repository riêng. Đây là lựa chọn đã cân nhắc: chuyển được nguyên văn các truy vấn đã chạy đúng, đổi lại Application phụ thuộc gói trừu tượng của EF Core (không phụ thuộc provider).
- **`ZaloZnsService` ghi database** — chỉ để lưu access token OAuth của chính nó (trạng thái kỹ thuật của bộ kết nối), không có nghiệp vụ.
- **`css/style.css` và `js/app.js` vẫn là một tệp lớn dùng chung.** `style.css` còn vài khối chỉ phục vụ một trang (xếp hạng, tổng quan…), nhưng chuyển chúng sang `pages/<trang>/` là đổi thứ tự cascade so với các khối chế độ tối/màn hình hẹp đứng sau — phải tách kèm kiểm tra giao diện từng trang, không chuyển tay.

## Các thư mục ngoài mã nguồn

- `Database/` — script tạo CSDL (`hbelearning.mssql.sql`, `01_create_oracle_user.sql`), sơ đồ ERD `erd.mermaid`, `backups/` (bản sao Sqlite cũ, tháng 8/2026). Database Sqlite đang dùng khi chạy vẫn là `API/hbelearning.db` (đường dẫn tương đối trong `appsettings.json`).
- `Docs/` — sơ đồ luồng hệ thống `SystemFlow/` (StarUML), ảnh gốc độ phân giải cao của `Mascot/` (bản dùng trên web ở `wwwroot/emoji/mascot/`), `archive/` (bản lưu cũ của trang đăng nhập).
- `Scripts/` — pipeline dựng mô hình nhân vật 3D (`build-character.mjs`, chạy bằng Node; `package.json` nằm ở gốc `HbELearning/`).
