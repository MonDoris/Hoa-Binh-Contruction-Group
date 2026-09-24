// File: Application/Common/Models/WordSectionResult.cs
// Kết quả tách MỘT file Word thành NHIỀU phần, mỗi phần sẽ thành một bài học.

namespace HbELearning.Application.Common.Models;

/// <param name="Title">Tên phần, lấy từ chính dòng tiêu đề mở đầu phần đó. null với phần nằm TRƯỚC dòng
/// tiêu đề đầu tiên (trang bìa, mục lục, lời nói đầu) — nơi gọi tự đặt tên cho phần này.</param>
/// <param name="Html">Nội dung HTML của riêng phần đó, đã bọc sẵn khung định dạng của tài liệu.</param>
/// <param name="CharacterCount">Số ký tự chữ (đã bỏ thẻ HTML) — để người soạn nhìn thấy phần nào dài ngắn
/// ra sao trước khi đồng ý tạo bài học.</param>
/// <param name="ImageCount">Số ảnh trong phần. Đếm cả khi không lưu ảnh (lượt xem trước), nhờ vậy danh sách
/// phần ở bước xem trước và ở bước tạo thật luôn TRÙNG KHỚP về số lượng và thứ tự.</param>
public record WordSection(string? Title, string Html, int CharacterCount, int ImageCount);

/// <param name="SectionList">Các phần theo đúng thứ tự trong tài liệu. Chỉ có một phần tử nghĩa là tài liệu
/// không có dấu hiệu tách nào — cả file là một bài học.</param>
/// <param name="ImageCount">Tổng số ảnh đã trích ra và lưu lại.</param>
/// <param name="SplitRule">Dấu hiệu đã dùng để tách, để giải thích cho người soạn: "heading" (tiêu đề cấp
/// cao nhất), "pattern" (dòng mở đầu bằng "Bài 1", "Chương 2"...), "none" (không tách).</param>
public record WordSectionResult(IReadOnlyList<WordSection> SectionList, int ImageCount, string SplitRule);
