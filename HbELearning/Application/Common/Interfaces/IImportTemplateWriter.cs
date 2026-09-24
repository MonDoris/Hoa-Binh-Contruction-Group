// File: Application/Common/Interfaces/IImportTemplateWriter.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>Nội dung một file mẫu nhập liệu — thứ mà phần đọc file phụ thuộc hoàn toàn (thứ tự cột, ý nghĩa cột).</summary>
/// <param name="Title">Tiêu đề in đầu bản Word.</param>
/// <param name="SheetName">Tên trang tính chứa dữ liệu trong bản Excel.</param>
/// <param name="Header">Tiêu đề từng cột, đúng thứ tự mà bộ nhập đọc.</param>
/// <param name="Sample">Các dòng ví dụ (người dùng xóa trước khi nhập thật).</param>
/// <param name="Guide">Hướng dẫn từng cột: (tên cột, ghi chú). Dòng ("", "") là dòng trống ngăn nhóm.</param>
/// <param name="ColumnWidthList">Độ rộng từng cột dữ liệu trong bản Excel, theo thứ tự cột.</param>
public sealed record ImportTemplate(
    string Title,
    string SheetName,
    string[] Header,
    string[][] Sample,
    (string Field, string Note)[] Guide,
    double[] ColumnWidthList);

/// <summary>Cách đánh dấu đáp án đúng mà file mẫu đề thi làm mẫu — mỗi câu ví dụ dùng một kiểu, để người soạn
/// thấy tận mắt là kiểu nào cũng nhập được và chọn kiểu mình quen tay.</summary>
public enum ExamAnswerMark
{
    /// <summary>Tô nền vàng bằng bút bôi màu — kiểu hay gặp nhất.</summary>
    Highlight,
    /// <summary>In đậm cả dòng đáp án.</summary>
    Bold,
    /// <summary>Thêm dấu * ở đầu dòng đáp án.</summary>
    Star,
    /// <summary>Viết riêng một dòng "Đáp án: B" ngay dưới các đáp án.</summary>
    KeyLine
}

/// <param name="Content">Nội dung câu hỏi, KHÔNG kèm "Câu n:" — bộ dựng file tự đánh số.</param>
/// <param name="CorrectIndexList">Vị trí (đếm từ 0) các đáp án đúng trong <paramref name="AnswerList"/>.</param>
public sealed record ExamSampleQuestion(
    string Content, string[] AnswerList, int[] CorrectIndexList, ExamAnswerMark Mark,
    string? Explanation = null, decimal? Score = null);

/// <param name="Title">Dòng chia bài ("BÀI 1: ..."), null = file chỉ dành cho đúng một bài kiểm tra nên
/// không cần chia.</param>
public sealed record ExamSampleSection(string? Title, ExamSampleQuestion[] QuestionList);

/// <summary>
/// Nội dung một file mẫu dạng ĐỀ SOẠN TỰ DO — thứ mà người soạn đề vẫn gõ trong Word: dòng chia bài, "Câu 1:",
/// các đáp án "A. B. C.", đáp án đúng được đánh dấu.
///
/// Khác hẳn <see cref="ImportTemplate"/> (bảng có sẵn cột): file này không có cột nào cả, người soạn gõ tiếp
/// vào chính nó theo đúng cách họ vẫn soạn đề.
/// </summary>
/// <param name="NoteList">Vài dòng dặn dò in ngay dưới tiêu đề, trước phần đề ví dụ.</param>
public sealed record ExamTemplate(
    string Title,
    string[] NoteList,
    ExamSampleSection[] SectionList,
    (string Field, string Note)[] Guide);

/// <summary>
/// Đóng gói nội dung file mẫu thành tệp Excel hoặc Word để người dùng tải về.
///
/// Nội dung (cột nào, ví dụ gì, hướng dẫn ra sao) là quy ước nghiệp vụ nên nằm ở Application; còn tô màu tiêu đề,
/// đóng băng dòng đầu, dựng bảng Word là chi tiết định dạng tệp, nằm ở Infrastructure.
/// </summary>
public interface IImportTemplateWriter
{
    byte[] BuildExcel(ImportTemplate template);

    byte[] BuildWord(ImportTemplate template);

    /// <summary>File mẫu Word dạng đề soạn tự do (xem <see cref="ExamTemplate"/>) — dành cho lối nhập đọc đề
    /// gõ tay, không phải lối nhập đọc bảng.</summary>
    byte[] BuildWordExam(ExamTemplate template);
}
