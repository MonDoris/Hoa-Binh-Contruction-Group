// File: API/Program.cs
using HbELearning.API.Extensions;
// Program.cs là nơi DUY NHẤT của tầng API được tham chiếu Infrastructure (xem API/GlobalUsings.cs).
using HbELearning.Infrastructure;
using HbELearning.Infrastructure.Persistence.Initialization;

var builder = WebApplication.CreateBuilder(args);

// ==================== LẮP RÁP (COMPOSITION ROOT) ====================
// Đây là nơi DUY NHẤT trong cả giải pháp biết đủ cả bốn lớp. Thứ tự ba dòng dưới đi từ trong ra ngoài,
// đúng theo chiều phụ thuộc của Clean Architecture:
//   Domain          - không đăng ký gì, vì không phụ thuộc gì (xem HbELearning.Domain.csproj).
//   Application     - nghiệp vụ, chỉ nói chuyện qua các cổng trong Application/Common/Interfaces.
//   Infrastructure  - nối từng cổng đó với một lớp hiện thực cụ thể (EF Core, SMTP, Ollama...).
//   API             - MVC, Swagger, JWT, SignalR, CORS.
// Muốn đổi database hay đổi nhà cung cấp Email thì chỉ sửa AddInfrastructure, không đụng tới tệp này.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, args)
    .AddApiServices(builder.Configuration);

var app = builder.Build();

// ==================== KHỞI TẠO DATABASE ====================
// Dựng/vá lược đồ rồi nạp dữ liệu nền, TRƯỚC khi đường ống bắt đầu nhận yêu cầu.
// Chi tiết ở Infrastructure/Persistence/Initialization/DatabaseInitializer.cs.
await DatabaseInitializer.InitializeAsync(app.Services, app.Environment);

// ==================== ĐƯỜNG ỐNG XỬ LÝ YÊU CẦU ====================
// Chi tiết ở API/Extensions/WebApplicationExtensions.cs.
app.UseApiPipeline();

app.Run();
