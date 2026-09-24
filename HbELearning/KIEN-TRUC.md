# HbELearning — Clean Architecture

Giải pháp gồm **4 project**, xếp từ trong ra ngoài. Mũi tên là chiều được phép phụ thuộc:

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

Các quy tắc trên **do trình biên dịch cưỡng chế**, không chỉ là quy ước:
- `Application.csproj` không tham chiếu Infrastructure lẫn framework ASP.NET → gõ `AppDbContext`, `IFormFile`, `HttpContext` trong Application sẽ không biên dịch được.
- `API/GlobalUsings.cs` không mở namespace nào của Infrastructure; chỉ `Program.cs` tự `using` → gõ `AppDbContext` trong một Controller sẽ không biên dịch được.

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
- `Features/<ChứcNăng>/` — **ca sử dụng** mà API gọi: `I<X>Service` + `<X>Service`, mỗi chức năng một thư mục (33 thư mục, ứng với 33 Controller).
- `Services/` — nghiệp vụ **dùng chung** giữa nhiều chức năng: `LearningService`, `RankingEngine`, `NotificationDispatcher`, `CertificateIssuer`, `DataImportService`, `DocumentProcessingService`, `CourseKnowledgeService`, `StudyReminderDispatcher`.
- `Common/Interfaces/` — **chỉ các cổng ra bên ngoài** (20 cổng), mỗi cổng được hiện thực ở Infrastructure hoặc API. VD `IAppDbContext`, `ICurrentUser`, `IFileService`, `IPasswordHasher`, `IEventStream`, `IChatNotifier`.
- `Common/Results/Result.cs` — kết quả ca sử dụng, mô tả bằng nghĩa nghiệp vụ (`Invalid`, `NotFound`, `Forbidden`…), không bằng mã HTTP.
- `Common/Models/` — `FileUpload` (thay `IFormFile`), `ImportRow`, `ImportTemplate`…
- `DTOs/` — hợp đồng dữ liệu vào/ra.

**`Infrastructure/`** — mỗi lớp là phần cắm vào cho đúng một cổng.
- `Persistence/` — `AppDbContext`, `DbProviderSetup`, `Seeding/`, `Initialization/DatabaseInitializer`.
- `Identity/` (JWT, BCrypt), `Ai/` (Ollama), `Documents/` (bóc chữ PDF/Word, Word→HTML), `Import/` (đọc Excel/Word/PDF, ghi file mẫu), `Certificates/` (ảnh QR), `Notifications/` (SMTP, eSMS, Zalo), `Files/`, `BackgroundJobs/` (chỉ vòng hẹn giờ — nghiệp vụ nằm ở Application).

**`API/`** — `Controllers/`, `Hubs/`, `Authorization/`, `Extensions/`, `wwwroot/`, `Program.cs`, và `Services/` gồm các hiện thực cổng thuộc về tầng web: `CurrentUser` (claim JWT), `ServerSentEventStream` (SSE), `SignalRChatNotifier`.

## Thêm một tính năng thì sửa ở đâu

1. Thực thể mới → `Domain/Entities/`; khai báo `DbSet` ở **cả** `AppDbContext` **và** `IAppDbContext`. Quy tắc chuyển trạng thái → phương thức trên thực thể.
2. Ca sử dụng mới → `Application/Features/<ChứcNăng>/`, trả về `Result`; đăng ký trong `Application/DependencyInjection.cs`.
3. Cần gọi ra ngoài (gửi thư, gọi API, đọc tệp, đẩy thời gian thực) → khai báo cổng ở `Application/Common/Interfaces/`, hiện thực ở `Infrastructure/` (hoặc `API/Services/` nếu là việc của tầng web), đăng ký DI tương ứng.
4. Điểm cuối HTTP → `API/Controllers/`, thân action chỉ gọi service và `ToActionResult`.

`Program.cs` chỉ còn phần lắp ráp; đừng thêm logic vào đó.

## Những gì cố ý giữ nguyên

- **Application dùng EF Core qua `IAppDbContext`** (có `DbSet<T>`), không viết repository riêng. Đây là lựa chọn đã cân nhắc: chuyển được nguyên văn các truy vấn đã chạy đúng, đổi lại Application phụ thuộc gói trừu tượng của EF Core (không phụ thuộc provider).
- **`ZaloZnsService` ghi database** — chỉ để lưu access token OAuth của chính nó (trạng thái kỹ thuật của bộ kết nối), không có nghiệp vụ.
- **`WeatherForecastController` + `WeatherForecast.cs`** là mã mẫu của .NET, không dùng tới — xóa được bất cứ lúc nào.
