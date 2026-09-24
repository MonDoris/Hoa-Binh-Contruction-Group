// File: Application/Common/Interfaces/IWordQuestionParser.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Đọc một file Word ngân hàng câu hỏi SOẠN TỰ DO (kiểu đề thi) thành các nhóm câu hỏi theo từng bài.
///
/// Khác với đường nhập theo file mẫu (IImportFileReader, mỗi dòng một câu, các cột ngăn bằng dấu gạch đứng),
/// bộ này đọc đúng thứ mà người soạn đề vẫn gõ:
///
///   BÀI 1: KIẾN THỨC CƠ BẢN VỀ PCCC       ← dòng chia bài
///   Câu 1: Cháy là gì?                     ← câu hỏi
///   A. Phản ứng cháy có sinh nhiệt         ← đáp án
///   B. Hiện tượng đông đặc
///   Đáp án: A                              ← đáp án đúng
///
/// Nhận đáp án đúng theo bất kỳ cách đánh dấu nào trong bốn cách phổ biến:
///   • dòng "Đáp án: A" ngay dưới câu hỏi;
///   • đáp án được in đậm / gạch chân / TÔ NỀN (bút bôi màu hay nền đoạn văn đều được);
///   • đáp án có dấu * (hay chữ "(đúng)") ở đầu;
///   • BẢNG ĐÁP ÁN đặt ở cuối đề ("ĐÁP ÁN" rồi "1. A  2. B  3. C", dạng đoạn văn hay dạng bảng đều được),
///     kể cả khi mỗi bài có một bảng riêng ("ĐÁP ÁN BÀI 2").
///
/// Đoạn văn dẫn nhập đầu tài liệu ("Hướng dẫn: ...", "Thời gian làm bài: 45 phút") không có đáp án nào nên
/// được bỏ qua lặng lẽ, không bị báo là câu hỏi lỗi.
///
/// Cách đánh dấu bằng định dạng chữ (đậm, gạch chân, tô nền) chỉ tính khi nó PHÂN BIỆT được: cả bốn đáp án
/// cùng được tô thì đó là kiểu trình bày của cả tài liệu, không phải dấu chỉ đáp án đúng.
///
/// KHÔNG chặn lần nhập vì một câu đọc thiếu: câu hỏi có đủ đáp án nhưng không tìm ra đáp án đúng vẫn được
/// trả về, mang cờ <see cref="ParsedQuestion.NeedsAnswerReview"/> để người soạn tick lại trong ngân hàng câu
/// hỏi. Một file đề vài trăm câu mà hỏng vì vài câu viết lạ thì người soạn phải sửa Word rồi nhập lại từ
/// đầu — tốn hơn nhiều so với việc tick lại vài ô.
///
/// Trong cùng một file vẫn đọc được BẢNG soạn theo file mẫu — nhiều đơn vị trộn cả hai cách trong một tài liệu.
/// </summary>
public interface IWordQuestionParser
{
    WordQuestionDocument Parse(Stream docxStream);
}
