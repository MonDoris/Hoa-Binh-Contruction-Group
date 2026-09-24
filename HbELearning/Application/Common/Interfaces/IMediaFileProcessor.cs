// File: Application/Common/Interfaces/IMediaFileProcessor.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Xử lý nhị phân trên tệp âm thanh/video đã lưu.
///
/// Nghiệp vụ phòng học online từng gọi thẳng WebmDurationFixer — một bộ đọc/ghi định dạng WebM nằm ở
/// Infrastructure. Giờ nghiệp vụ chỉ nói "vá thời lượng cho bản ghi này", còn cách vá là việc của hạ tầng.
/// </summary>
public interface IMediaFileProcessor
{
    /// <summary>Ghi lại trường Duration trong header của tệp WebM do trình duyệt ghi hình (MediaRecorder bỏ
    /// trống trường này, khiến trình phát không tua được). Trả false nếu tệp không vá được.</summary>
    bool PatchWebmDuration(string filePath, double durationMs);
}
