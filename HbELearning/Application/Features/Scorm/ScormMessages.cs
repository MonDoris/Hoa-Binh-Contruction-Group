// File: Application/Features/Scorm/ScormMessages.cs

namespace HbELearning.Application.Features.Scorm;

/// <summary>Đổi mã ngoại lệ của chuẩn SN (VD "SB.2.2-2") thành câu học viên đọc hiểu được. Mã vẫn được giữ ở cuối
/// câu để người hỗ trợ tra lại đúng bước của chuẩn khi cần.</summary>
public static class ScormMessages
{
    public static string? FromException(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var text = code switch
        {
            "SB.2.2-2" or "DB.1.1-3" => "Nội dung này đang bị khóa — chưa đủ điều kiện mở, hoặc đã hết số lần học cho phép.",
            "SB.2.4-1" => "Gói không cho nhảy vượt tới mục này — hãy học lần lượt.",
            "SB.2.4-2" => "Phần này chỉ cho đi tới, không cho quay lại.",
            "SB.2.9-3" => "Mục này hiện đang bị ẩn.",
            "SB.2.9-4" or "NB.2.1-10" => "Phần này không cho tự chọn mục — hãy dùng nút Tiếp / Trước.",
            "SB.2.9-6" => "Chưa được mở mục này từ vị trí hiện tại.",
            "SB.2.9-7" or "NB.2.1-8" => "Phải hoàn tất phần đang học rồi mới rời sang phần khác được.",
            "SB.2.9-8" => "Chỉ được chọn các mục kế cận phần đang học.",
            "SB.2.9-9" => "Phần này không còn mục nào mở được.",
            "NB.2.1-4" or "SB.2.7-2" or "SB.2.2-1" => "Gói không cho bấm Tiếp ở đây — hãy chọn mục trong mục lục.",
            "NB.2.1-5" or "SB.2.8-2" => "Gói không cho quay lại ở đây.",
            "NB.2.1-6" or "SB.2.1-3" => "Đây đã là mục đầu tiên.",
            "SB.2.5-1" or "NB.2.1-1" => "Phiên học đã bắt đầu.",
            "SB.2.1-2" => "Phần này không có mục nào để học.",
            _ => "Không chuyển được tới nội dung yêu cầu.",
        };
        return $"{text} ({code})";
    }
}
