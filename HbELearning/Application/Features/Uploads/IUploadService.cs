// File: Application/Features/Uploads/IUploadService.cs

namespace HbELearning.Application.Features.Uploads;

/// <summary>Các ca sử dụng của chức năng Upload. Tầng API gọi qua cổng này (xem API/Controllers/UploadService.cs).</summary>
public interface IUploadService
{
    /// <summary>Tải lên tài liệu / hình ảnh / video bài học. Trả về URL để gán vào Lesson.ResourceUrl.
    /// Ảnh đại diện ("avatar") thì ai cũng tải lên được cho chính mình; các thư mục khác chỉ Admin/Trainer.
    /// "scenario" là ảnh/video minh họa cho từng tình huống của mô phỏng 3D (ScenarioNode.MediaUrl). Tệp
    /// BỐI CẢNH của mô phỏng thì không còn tải lên qua đây nữa: bối cảnh chọn từ Thư viện 3D dùng chung,
    /// nằm ở thư mục "sim-library" và do SimAssetService quản.</summary>
    Task<Result> Upload(FileUpload file, string subFolder = "lesson");

    Result Delete(string url);
}
