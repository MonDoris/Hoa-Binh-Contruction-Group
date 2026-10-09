// File: API/Extensions/WebApplicationExtensions.cs

namespace HbELearning.API.Extensions;

/// <summary>Đường ống middleware — thứ tự các dòng trong hàm này là thứ tự mỗi yêu cầu HTTP đi qua.</summary>
public static class WebApplicationExtensions
{
    /// <summary>Lắp đường ống xử lý yêu cầu và gắn các điểm cuối (Controller, SignalR Hub).</summary>
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            // Lỗi không ai bắt: ghi nhật ký đầy đủ, trả ra ngoài một câu chung chung kèm mã tra cứu.
            // Chỉ lắp khi chạy thật — môi trường phát triển vẫn dùng trang lỗi chi tiết của ASP.NET để
            // người lập trình đọc được ngay chỗ hỏng.
            app.UseSafeExceptionHandler();

            // Nói với trình duyệt: từ nay chỉ gọi tên miền này qua HTTPS. Không có dòng này thì lần gõ địa chỉ
            // đầu tiên vẫn đi bằng HTTP và thẻ đăng nhập có thể bị đọc trộm ngay trên đường truyền trước khi
            // UseHttpsRedirection kịp chuyển hướng.
            app.UseHsts();
        }

        // Đặt TRƯỚC mọi thứ trả nội dung ra ngoài, để cả tệp tĩnh lẫn phản hồi API đều mang đủ đầu mục bảo vệ.
        app.UseSecurityHeaders();

        app.UseHttpsRedirection();

        // Frontend tĩnh trong wwwroot
        app.UseDefaultFiles();

        // UseStaticFiles mặc định CHỈ phục vụ các đuôi file "quen" (html/css/js/png...) — .glb/.gltf/.fbx
        // không nằm trong danh sách đó nên bị trả 404 dù file có thật trên đĩa, dùng cho model 3D thật
        // ở tính năng Mô phỏng 3D (xem wwwroot/models/). Khai báo thêm để phục vụ được.
        var modelContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        modelContentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
        modelContentTypeProvider.Mappings[".gltf"] = "model/gltf+json";
        modelContentTypeProvider.Mappings[".fbx"] = "application/octet-stream";
        modelContentTypeProvider.Mappings[".obj"] = "text/plain";
        modelContentTypeProvider.Mappings[".mtl"] = "text/plain";

        // Bảng kê ứng dụng (PWA). Có bản đồ sẵn trong danh sách mặc định của ASP.NET, khai báo lại ở đây cho rõ
        // ràng — sai kiểu MIME thì Chrome bỏ qua bảng kê và mất luôn nút "Cài ứng dụng", mà không báo lỗi gì.
        modelContentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = modelContentTypeProvider,
            OnPrepareResponse = context =>
            {
                // Môi trường phát triển: tắt cache để sửa HTML/CSS/JS là thấy ngay,
                // không phải nhấn Ctrl+F5 sau mỗi lần chỉnh.
                if (app.Environment.IsDevelopment())
                {
                    context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    context.Context.Response.Headers.Pragma = "no-cache";
                    context.Context.Response.Headers.Expires = "0";
                }

                // sw.js KHÔNG bao giờ được cache, kể cả khi chạy thật. Trình duyệt lấy đúng file này để biết có
                // bản mới hay không — nếu chính nó bị giữ trong cache thì người dùng kẹt vĩnh viễn ở bản đã cài,
                // sửa gì trên máy chủ cũng không tới được máy họ.
                else if (context.Context.Request.Path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                }
            }
        });

        // Nội dung các gói SCORM (App_Data/scorm/{khóa}/...) ở /scorm-content/{khóa}/... — CÙNG nguồn gốc với trang
        // để SCO trong iframe tìm được bộ API (API_1484_11 / API) trên cửa sổ trình phát: khác nguồn gốc là trình
        // duyệt chặn SCO đọc thuộc tính của cửa sổ cha, mà đó là cách DUY NHẤT chuẩn SCORM cho SCO tìm LMS.
        //
        // Gói SCORM là cả một website với đủ loại tệp (font, phụ đề .vtt, dữ liệu .json, tệp riêng của công cụ soạn
        // bài) — phục vụ cả đuôi lạ, dưới dạng application/octet-stream để nosniff chặn trình duyệt đoán kiểu.
        var scormRoot = HbELearning.Infrastructure.Files.ScormPackageStorage.ResolveRoot(app.Environment.ContentRootPath);
        Directory.CreateDirectory(scormRoot);
        var scormContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        scormContentTypeProvider.Mappings[".vtt"] = "text/vtt";
        scormContentTypeProvider.Mappings[".srt"] = "text/plain";
        scormContentTypeProvider.Mappings[".mjs"] = "text/javascript";
        scormContentTypeProvider.Mappings[".xsd"] = "text/xml";
        scormContentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(scormRoot),
            RequestPath = "/scorm-content",
            ContentTypeProvider = scormContentTypeProvider,
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
            OnPrepareResponse = context =>
            {
                // Thư mục gói là GUID, nội dung không bao giờ đổi tại chỗ (đổi gói = nhập gói mới, thư mục mới) —
                // cho trình duyệt giữ một giờ để mỗi lần bấm Tiếp không phải tải lại cả bộ thư viện của gói.
                context.Context.Response.Headers.CacheControl = app.Environment.IsDevelopment()
                    ? "no-cache" : "private, max-age=3600";
            }
        });

        app.UseCors(ApiServiceExtensions.CorsPolicyName);

        // Đứng TRƯỚC xác thực: một trận dò mật khẩu phải bị chặn từ vòng đếm, chứ không phải sau khi đã tốn
        // một lượt băm BCrypt cho mỗi lần thử.
        app.UseRateLimiter();

        app.UseAuthentication();   // BẮT BUỘC đứng trước UseAuthorization
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<ClassRoomHub>("/hubs/classroom");
        app.MapHub<ChatHub>("/hubs/chat");

        return app;
    }
}
