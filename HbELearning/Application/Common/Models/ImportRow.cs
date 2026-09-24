// File: Application/Common/Models/ImportRow.cs
namespace HbELearning.Application.Common.Models;

/// <summary>Định dạng file mà chức năng nhập dữ liệu hàng loạt chấp nhận.</summary>
public enum ImportFormat { Excel, Word, Pdf }

/// <summary>
/// Một dòng dữ liệu đã được tách sẵn thành các ô, KHÔNG còn dính gì tới định dạng file nguồn.
///
/// Có mặt để phần kiểm tra và ghi dữ liệu (ImportLessonAsync / ImportQuestionAsync) chỉ phải viết một lần:
/// Excel đọc theo ô, Word đọc theo ô của bảng, PDF tách theo dấu gạch đứng — ba cách đọc khác hẳn nhau
/// nhưng đều quy về đúng một danh sách chuỗi, nên quy tắc "cột 1 là tiêu đề, cột 9 là đáp án đúng..."
/// không bị chép lại ba lần rồi lệch nhau lúc sửa.
/// </summary>
/// <param name="Number">Số dòng để báo lỗi cho người dùng — số dòng thật trong file nguồn.</param>
public sealed record ImportRow(int Number, IReadOnlyList<string> CellList)
{
    /// <summary>Nội dung ô theo số cột đếm từ 1. Cột vượt quá số ô của dòng trả về chuỗi rỗng —
    /// dòng cuối trong Word/PDF thường thiếu vài ô trống ở đuôi.</summary>
    public string Text(int column) =>
        column >= 1 && column <= CellList.Count ? CellList[column - 1].Trim() : string.Empty;

    /// <summary>Số nguyên ở ô, 0 nếu ô trống hoặc không phải số. Cắt phần thập phân do Excel hay trả "20.0".</summary>
    public int Int(int column)
    {
        var raw = Text(column);
        if (raw.Length == 0) return 0;

        var dot = raw.IndexOfAny(new[] { '.', ',' });
        if (dot >= 0) raw = raw[..dot];

        return int.TryParse(raw, out var value) ? value : 0;
    }

    /// <summary>Dòng không có ô nào chứa chữ — bỏ qua, không tính là dòng lỗi.</summary>
    public bool IsEmpty => CellList.All(string.IsNullOrWhiteSpace);
}

/// <summary>Nhận dạng định dạng từ phần mở rộng của file tải lên.</summary>
public static class ImportFormatExtensions
{
    /// <summary>Trả null nếu phần mở rộng không nằm trong danh sách hỗ trợ.</summary>
    public static ImportFormat? FromFileName(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" => ImportFormat.Excel,
            ".docx" => ImportFormat.Word,
            ".pdf" => ImportFormat.Pdf,
            _ => null
        };

    /// <summary>Đọc tham số ?format= của lệnh tải file mẫu. Không nhận ra thì mặc định Excel.</summary>
    public static ImportFormat FromQuery(string? raw) => (raw ?? string.Empty).ToLowerInvariant() switch
    {
        "word" or "docx" => ImportFormat.Word,
        "pdf" => ImportFormat.Pdf,
        _ => ImportFormat.Excel
    };

    public static string ToVietnameseLabel(this ImportFormat format) => format switch
    {
        ImportFormat.Word => "Word",
        ImportFormat.Pdf => "PDF",
        _ => "Excel"
    };
}
