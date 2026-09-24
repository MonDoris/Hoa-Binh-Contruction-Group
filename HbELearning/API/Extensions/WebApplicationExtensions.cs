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

        app.UseCors("AllowAll");

        app.UseAuthentication();   // BẮT BUỘC đứng trước UseAuthorization
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<ClassRoomHub>("/hubs/classroom");
        app.MapHub<ChatHub>("/hubs/chat");

        return app;
    }
}
