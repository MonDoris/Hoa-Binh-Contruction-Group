// File: Application/Features/Import/IImportService.cs

namespace HbELearning.Application.Features.Import;

/// <summary>Các ca sử dụng của chức năng Import. Tầng API gọi qua cổng này (xem API/Controllers/ImportService.cs).</summary>
public interface IImportService
{
    /// <summary>Tải file mẫu để nhập câu hỏi hàng loạt. format = excel (mặc định) | word.</summary>
    Result DownloadQuestionTemplate(string? format);

    /// <summary>Tải file mẫu nhập câu hỏi cho CẢ KHÓA HỌC — cùng bộ cột với file mẫu trên, thêm cột
    /// "Bài kiểm tra" ở đầu. format = excel (mặc định) | word.</summary>
    Result DownloadQuestionSheetTemplate(string? format);

    /// <summary>Tải file mẫu để nhập bài học hàng loạt. format = excel (mặc định) | word.</summary>
    Result DownloadLessonTemplate(string? format);

    Task<Result> ImportQuestion(int quizId, FileUpload file);

    /// <summary>
    /// Nhập MỘT file Word ngân hàng câu hỏi soạn tự do cho cả khóa học: tự tách theo dòng chia bài
    /// ("BÀI 1: ...") rồi đổ câu hỏi của từng bài vào bài kiểm tra tương ứng — khớp tên với bài kiểm tra /
    /// bài học đã có, không khớp thì tạo bài kiểm tra mới (xem <see cref="IWordQuestionParser"/>).
    /// </summary>
    /// <param name="sectionIndexList">Số thứ tự các nhóm người soạn đã tick ở hộp thoại xem trước. Bỏ trống = lấy hết.</param>
    Task<Result> ImportQuestionDocument(int courseId, FileUpload file, IReadOnlyList<int>? sectionIndexList = null);

    /// <summary>Xem trước các nhóm câu hỏi trong file Word và nơi từng nhóm sẽ được đổ vào — KHÔNG ghi gì.</summary>
    Task<Result> GetQuestionOutline(int courseId, FileUpload file);

    /// <summary>Nhập file Word đề thi tự do vào đúng một bài kiểm tra (dùng từ hộp thoại Ngân hàng câu hỏi).</summary>
    Task<Result> ImportQuestionDocumentToQuiz(int quizId, FileUpload file);

    /// <summary>
    /// Nhập MỘT bảng câu hỏi soạn theo file mẫu cả khóa học (Excel, hoặc bảng trong Word / PDF): gom các dòng
    /// theo cột "Bài kiểm tra" rồi đổ từng nhóm vào bài kiểm tra tương ứng.
    ///
    /// Giống <see cref="ImportQuestionDocument"/> ở nửa sau — khớp tên nhóm với bài kiểm tra / bài học đã có,
    /// không khớp thì tạo bài kiểm tra mới — chỉ khác cách đọc file: bên kia đọc đề soạn tự do, bên này đọc
    /// bảng có sẵn cột.
    /// </summary>
    /// <param name="sectionIndexList">Số thứ tự các nhóm người soạn đã tick ở hộp thoại xem trước. Bỏ trống = lấy hết.</param>
    Task<Result> ImportQuestionSheet(int courseId, FileUpload file, IReadOnlyList<int>? sectionIndexList = null);

    /// <summary>Xem trước các nhóm trong bảng câu hỏi và bài kiểm tra sẽ nhận chúng — KHÔNG ghi gì.</summary>
    Task<Result> GetQuestionSheetOutline(int courseId, FileUpload file);

    Task<Result> ImportLesson(int courseId, FileUpload file);

    /// <summary>
    /// Đọc NGUYÊN một file Word/PowerPoint/PDF và dựng lại thành MỘT bài học trông giống bản gốc. Khác hẳn hai lệnh
    /// nhập ở trên: hai lệnh kia đọc một BẢNG dữ liệu để tạo nhiều bài học, còn lệnh này lấy cả tài liệu
    /// làm nội dung của đúng một bài.
    ///
    /// Hai định dạng đi hai đường khác nhau, vì bản chất của chúng khác nhau:
    ///   • Word: bóc tách thành HTML giữ nguyên tiêu đề, in đậm, màu, danh sách, bảng, ảnh — nội dung nằm
    ///     hẳn trong hệ thống nên tìm kiếm được, sửa được, và hiện thẳng trong trang học.
    ///   • PowerPoint: mỗi slide dựng thành một khung đúng tỉ lệ, chữ và hình đặt đúng vị trí, kèm ghi chú
    ///     của người trình bày; file .pptx gốc được đính kèm để tải về (xem IPowerPointHtmlConverter).
    ///   • PDF: lưu nguyên file rồi nhúng vào bài học. PDF là định dạng dàn trang cố định, chuyển sang HTML
    ///     kiểu gì cũng lệch bản gốc; nhúng nguyên file mới đúng nghĩa "giống hệt".
    /// </summary>
    /// <param name="sectionIndexList">Chỉ dùng cho file Word tách được thành nhiều bài: số thứ tự các phần
    /// người soạn đã tick ở hộp thoại xem trước. Bỏ trống = tạo hết.</param>
    Task<Result> ImportDocument(int courseId, FileUpload file, IReadOnlyList<int>? sectionIndexList = null);

    /// <summary>Xem trước danh sách bài học sẽ tách ra từ một file Word — KHÔNG tạo bài học, không lưu ảnh.
    /// Người soạn nhìn danh sách rồi mới quyết định tạo phần nào (xem <see cref="ImportDocument"/>).</summary>
    Task<Result> GetDocumentOutline(FileUpload file);
}
