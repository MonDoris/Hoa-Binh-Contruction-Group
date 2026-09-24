// File: Application/Services/DataImportService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

/// <summary>
/// Nhập dữ liệu hàng loạt (câu hỏi, bài học, phòng ban, vị trí, người dùng) và nội dung các file mẫu tương ứng.
///
/// Tách từ ExcelImportService cũ ở Infrastructure, vốn trộn ba việc: quy tắc từng dòng (nghiệp vụ), đọc tệp bằng
/// ClosedXML/OpenXml/PdfPig (hạ tầng) và tô định dạng file mẫu (hạ tầng). Quy tắc ở lại đây, nguyên văn; đọc tệp đi
/// qua <see cref="IImportFileReader"/>, đóng gói file mẫu đi qua <see cref="IImportTemplateWriter"/>.
///
/// Cũng đổi tên: bộ này nhập cả Word và PDF chứ không chỉ Excel, và tên lớp nghiệp vụ không nên mang tên một
/// công nghệ lưu trữ.
/// </summary>
public class DataImportService : IDataImportService
{
    /// <summary>Số cột đáp án tối đa trong file mẫu câu hỏi (cột D → H).</summary>
    private const int MaxAnswerColumn = 5;

    private static readonly string[] AnswerLetter = { "A", "B", "C", "D", "E" };

    private readonly IAppDbContext _db;
    private readonly IImportFileReader _fileReader;
    private readonly IImportTemplateWriter _templateWriter;
    private readonly IPasswordHasher _passwordHasher;

    public DataImportService(IAppDbContext db, IImportFileReader fileReader, IImportTemplateWriter templateWriter,
        IPasswordHasher passwordHasher)
    {
        _db = db;
        _fileReader = fileReader;
        _templateWriter = templateWriter;
        _passwordHasher = passwordHasher;
    }

    // ==================================================================
    //  FILE MẪU
    // ==================================================================

    /// <summary>Bộ cột, dòng ví dụ và phần hướng dẫn của file mẫu câu hỏi — nguồn duy nhất cho cả bản Excel
    /// lẫn bản Word. Tách ra để hai định dạng không bao giờ lệch nhau về số cột hay thứ tự cột, thứ mà phần
    /// đọc file (ImportQuestionAsync) phụ thuộc hoàn toàn.</summary>
    private static (string[] Header, string[][] Sample, (string, string)[] Guide) QuestionTemplate() =>
    (
        new[]
        {
            "Nội dung câu hỏi (*)", "Kiểu câu hỏi", "Điểm",
            "Đáp án A (*)", "Đáp án B (*)", "Đáp án C", "Đáp án D", "Đáp án E",
            "Đáp án đúng (*)", "Giải thích"
        },
        new[]
        {
            new[]
            {
                "Dấu hiệu nào cảnh báo một email lừa đảo?", "Một lựa chọn", "1",
                "Người gửi lạ, yêu cầu gấp", "Email từ đồng nghiệp quen", "Có chữ ký công ty", "", "",
                "A", "Email lừa đảo thường tạo cảm giác khẩn cấp."
            },
            new[]
            {
                "Những hành vi nào vi phạm quy định bảo mật?", "Nhiều lựa chọn", "2",
                "Dùng chung tài khoản", "Ghi mật khẩu lên giấy dán màn hình", "Khóa máy khi rời chỗ", "Cài phần mềm lạ", "",
                "A,B,D", "Chỉ khóa máy khi rời chỗ là hành vi đúng."
            },
            new[]
            {
                "Nhân viên được phép chia sẻ tài khoản nội bộ cho đồng nghiệp.", "Đúng/Sai", "1",
                "Đúng", "Sai", "", "", "",
                "B", "Mỗi nhân viên phải dùng tài khoản riêng."
            }
        },
        new (string, string)[]
        {
            ("Nội dung câu hỏi (*)", "Bắt buộc. Nội dung hiển thị cho người học."),
            ("Kiểu câu hỏi", "Một lựa chọn | Nhiều lựa chọn | Đúng/Sai. Bỏ trống hệ thống tự nhận: nhiều đáp án đúng thì hiểu là Nhiều lựa chọn."),
            ("Điểm", "Số điểm của câu hỏi. Bỏ trống mặc định là 1."),
            ("Đáp án A → E", "Điền tối thiểu 2 đáp án. Cột nào trống thì bỏ qua."),
            ("Đáp án đúng (*)", "Ghi chữ cái cột đáp án đúng. Nhiều đáp án thì cách nhau dấu phẩy, ví dụ: A,C"),
            ("Giải thích", "Không bắt buộc. Hiển thị cho người học sau khi nộp bài."),
            ("", ""),
            ("LƯU Ý", "Xóa 3 dòng ví dụ trước khi nhập dữ liệu thật."),
            ("", "Kiểu Đúng/Sai bắt buộc đúng 2 đáp án và chỉ 1 đáp án đúng."),
            ("", "Dòng nào sai sẽ bị bỏ qua và báo lỗi kèm số dòng, các dòng còn lại vẫn được nhập.")
        }
    );

    /// <summary>
    /// File mẫu nhập câu hỏi cho MỘT bài kiểm tra.
    ///
    /// Hai định dạng KHÔNG cùng một hình dạng, vì hai lối nhập đọc hai kiểu file khác hẳn nhau:
    ///   • Excel → bảng có sẵn cột, đọc theo ô (ImportQuestionAsync);
    ///   • Word  → đề soạn tự do "Câu 1: ... A. ... B. ...", đọc bằng WordQuestionParser — đúng thứ người
    ///     soạn đề vẫn gõ sẵn trong Word, nên tải mẫu về là gõ tiếp được ngay, không phải chép từng câu vào
    ///     ô của một cái bảng.
    /// </summary>
    public byte[] BuildQuestionTemplate(ImportFormat format = ImportFormat.Excel)
    {
        if (format == ImportFormat.Word) return _templateWriter.BuildWordExam(QuestionExamTemplate(false));

        var (header, sample, guide) = QuestionTemplate();
        return _templateWriter.BuildExcel(new ImportTemplate("FILE MẪU NHẬP CÂU HỎI", "CauHoi", header, sample, guide,
            new double[] { 46, 16, 8, 28, 28, 28, 28, 28, 16, 40 }));
    }

    /// <summary>
    /// File mẫu nhập câu hỏi cho CẢ KHÓA HỌC: y hệt file mẫu của một bài kiểm tra, chỉ thêm cột "Bài kiểm tra"
    /// ở đầu để một file chứa được câu hỏi của nhiều bài. Cột thêm vào nằm ở đầu chứ không phải ở cuối để
    /// người điền nhìn thấy ngay mình đang soạn cho bài nào.
    ///
    /// Bộ cột phía sau lấy nguyên từ <see cref="QuestionTemplate"/> — sửa cột ở đó là cả hai file mẫu đổi
    /// theo, không có chuyện hai file lệch nhau về thứ tự cột (thứ mà phần đọc file phụ thuộc hoàn toàn).
    /// </summary>
    public byte[] BuildQuestionSheetTemplate(ImportFormat format = ImportFormat.Excel)
    {
        // Bản Word là ĐỀ SOẠN TỰ DO chia bài bằng dòng "BÀI 1: ...", không phải bảng — xem BuildQuestionTemplate.
        if (format == ImportFormat.Word) return _templateWriter.BuildWordExam(QuestionExamTemplate(true));

        var (header, sample, guide) = QuestionTemplate();

        var sheetHeader = new[] { "Bài kiểm tra (*)" }.Concat(header).ToArray();
        var sheetSample = new[]
        {
            new[] { "Bài 1 - Tổng quan an toàn thông tin" }.Concat(sample[0]).ToArray(),
            new[] { "Bài 1 - Tổng quan an toàn thông tin" }.Concat(sample[1]).ToArray(),
            new[] { "Bài 2 - Quy định bảo mật nội bộ" }.Concat(sample[2]).ToArray()
        };
        var sheetGuide = new (string, string)[]
        {
            ("Bài kiểm tra (*)", "Tên bài kiểm tra sẽ nhận câu hỏi. Các dòng cùng tên được gom về cùng một bài."),
            ("", "Trùng tên một bài kiểm tra đã có thì câu hỏi được thêm vào bài đó; trùng tên một BÀI HỌC thì "
                 + "hệ thống tạo bài kiểm tra gắn vào bài học ấy; không trùng gì cả thì tạo bài kiểm tra mới."),
            ("", "So tên bỏ qua hoa thường và dấu tiếng Việt, nên \"BÀI 1: AN TOÀN ĐIỆN\" vẫn khớp \"Bài 1 - An toàn điện\"."),
            ("", "")
        }.Concat(guide).ToArray();

        return _templateWriter.BuildExcel(new ImportTemplate("FILE MẪU NHẬP CÂU HỎI CHO CẢ KHÓA HỌC", "CauHoi",
            sheetHeader, sheetSample, sheetGuide,
            new double[] { 34, 46, 16, 8, 28, 28, 28, 28, 28, 16, 40 }));
    }

    /// <summary>
    /// Nội dung file mẫu Word dạng đề soạn tự do. Bốn câu ví dụ cố ý đánh dấu đáp án đúng bằng bốn kiểu khác
    /// nhau — tô nền, in đậm, dấu *, dòng "Đáp án:" — để người soạn thấy kiểu nào cũng nhập được và cứ giữ
    /// nguyên kiểu mình quen tay.
    /// </summary>
    /// <param name="withSection">true = file dành cho cả khóa học, có dòng chia bài "BÀI 1: ..." để tách câu
    /// hỏi về từng bài kiểm tra; false = file dành cho đúng một bài kiểm tra, mọi câu đổ vào bài đang mở nên
    /// dòng chia bài không có ý nghĩa gì.</param>
    private static ExamTemplate QuestionExamTemplate(bool withSection)
    {
        var noteList = new List<string>
        {
            "Gõ tiếp câu hỏi của bạn vào file này, hoặc dán nguyên đề có sẵn vào rồi xóa mấy câu ví dụ bên dưới.",
            "Đánh dấu đáp án đúng theo kiểu nào cũng được — bốn câu ví dụ dưới đây làm mẫu bốn kiểu.",
            "Phần chữ nghiêng này và mục HƯỚNG DẪN ở cuối file không bị nhập vào hệ thống, để nguyên cũng không sao."
        };
        if (withSection)
            noteList.Insert(1, "Mỗi bài kiểm tra mở đầu bằng một dòng \"BÀI 1: tên bài\" — các câu phía dưới "
                               + "thuộc về bài đó cho tới dòng \"BÀI\" kế tiếp.");

        var firstList = new[]
        {
            new ExamSampleQuestion(
                "Dấu hiệu nào cảnh báo một email lừa đảo?",
                new[]
                {
                    "Người gửi lạ, yêu cầu chuyển tiền gấp", "Email từ đồng nghiệp quen",
                    "Có chữ ký công ty ở cuối thư", "Gửi trong giờ hành chính"
                },
                new[] { 0 }, ExamAnswerMark.Highlight),

            new ExamSampleQuestion(
                "Khi nhận được email lạ có tệp đính kèm, việc nên làm đầu tiên là gì?",
                new[]
                {
                    "Mở tệp ra xem cho biết nội dung", "Báo bộ phận IT kiểm tra trước khi mở",
                    "Chuyển tiếp cho cả phòng cùng xem", "Trả lời hỏi lại người gửi"
                },
                new[] { 1 }, ExamAnswerMark.Bold,
                Explanation: "Tệp đính kèm là đường lây mã độc phổ biến nhất."),

            new ExamSampleQuestion(
                "Nhân viên được phép chia sẻ tài khoản nội bộ cho đồng nghiệp.",
                new[] { "Đúng", "Sai" },
                new[] { 1 }, ExamAnswerMark.Star),

            new ExamSampleQuestion(
                "Những hành vi nào vi phạm quy định bảo mật? (chọn nhiều đáp án)",
                new[]
                {
                    "Dùng chung tài khoản", "Ghi mật khẩu lên giấy dán màn hình",
                    "Khóa máy khi rời chỗ", "Cài phần mềm lạ do người khác gửi"
                },
                new[] { 0, 1, 3 }, ExamAnswerMark.KeyLine, Score: 2)
        };

        var secondList = new[]
        {
            new ExamSampleQuestion(
                "Khu vực nào trong văn phòng có nguy cơ cháy nổ cao nhất?",
                new[]
                {
                    "Hành lang và cầu thang bộ", "Sân thượng và nhà vệ sinh",
                    "Phòng máy chủ, khu vực đun nấu, kho chứa giấy tờ", "Khu vực lễ tân"
                },
                new[] { 2 }, ExamAnswerMark.Highlight),

            new ExamSampleQuestion(
                "Khi phát hiện đám cháy nhỏ mới bùng, việc đầu tiên phải làm là gì?",
                new[]
                {
                    "Chạy ra khỏi tòa nhà ngay", "Báo động và dùng bình chữa cháy tại chỗ",
                    "Quay phim lại làm bằng chứng", "Chờ bảo vệ tới xử lý"
                },
                new[] { 1 }, ExamAnswerMark.KeyLine)
        };

        var sectionList = withSection
            ? new[]
            {
                new ExamSampleSection("BÀI 1: AN TOÀN THÔNG TIN", firstList),
                new ExamSampleSection("BÀI 2: AN TOÀN PHÒNG CHÁY CHỮA CHÁY", secondList)
            }
            : new[] { new ExamSampleSection(null, firstList) };

        var guideList = new List<(string, string)>();
        if (withSection)
        {
            guideList.Add(("Dòng chia bài", "\"BÀI 1: ...\", \"CHƯƠNG 2 - ...\", \"PHẦN 3: ...\". Trùng tên một bài "
                + "kiểm tra đã có thì câu hỏi được thêm vào bài đó; trùng tên một BÀI HỌC thì hệ thống tạo bài "
                + "kiểm tra gắn vào bài học ấy; không trùng gì cả thì tạo bài kiểm tra mới."));
            guideList.Add(("", "So tên bỏ qua hoa thường và dấu tiếng Việt, nên \"BÀI 1: AN TOÀN ĐIỆN\" vẫn khớp "
                + "\"Bài 1 - An toàn điện\". Trước khi tạo thật, màn hình còn hiện bảng xem trước để bạn bỏ tick "
                + "bài không muốn nhập."));
        }

        guideList.AddRange(new (string, string)[]
        {
            ("Dòng câu hỏi", "Mở đầu bằng \"Câu 1:\", \"Câu hỏi 1 -\" hoặc chỉ \"1.\" cũng được."),
            ("Dòng đáp án", "Mỗi đáp án một dòng, mở đầu bằng A. B. C. D. (tối đa tới H). Viết \"A)\" hay \"(A)\" đều nhận."),
            ("Đánh dấu đáp án đúng", "Bốn kiểu, dùng kiểu nào cũng được, trộn lẫn trong cùng một file cũng được:"),
            ("", "1. Tô nền dòng đáp án đúng bằng bút bôi màu (như câu 1) — hoặc in đậm, gạch chân (như câu 2)."),
            ("", "2. Thêm dấu * ngay trước chữ cái đáp án đúng, ví dụ *B. (như câu 3)."),
            ("", "3. Viết riêng một dòng \"Đáp án: B\" ngay dưới các đáp án (như câu 4). Nhiều đáp án đúng thì ghi \"Đáp án: A, B, D\"."),
            ("", "4. Hoặc để nguyên một BẢNG ĐÁP ÁN ở cuối đề: một dòng \"ĐÁP ÁN\" rồi tới \"1. A  2. B  3. C\"."),
            ("Câu nhiều đáp án đúng", "Cứ đánh dấu từ 2 đáp án trở lên, hệ thống tự hiểu là câu nhiều lựa chọn."),
            ("Câu Đúng / Sai", "Viết đúng hai đáp án \"Đúng\" và \"Sai\" là hệ thống tự nhận đúng kiểu."),
            ("Giải thích", "Không bắt buộc. Một dòng \"Giải thích: ...\" ngay dưới câu, hiện cho người học sau khi nộp bài."),
            ("Điểm", "Không bắt buộc. Một dòng \"Điểm: 2\" ngay dưới câu. Bỏ trống thì mỗi câu 1 điểm."),
            ("", ""),
            ("LƯU Ý", "Câu nào hệ thống không tìm ra đáp án đúng vẫn được nhập, chỉ bị đánh dấu là chưa có đáp "
                + "án — bạn mở ngân hàng câu hỏi tick lại, không phải sửa file rồi nhập lại từ đầu."),
            ("", "File .doc (Office 97-2003) hãy mở bằng Word và lưu lại dạng .docx.")
        });

        var title = withSection
            ? "FILE MẪU NHẬP CÂU HỎI TỪ WORD - CẢ KHÓA HỌC"
            : "FILE MẪU NHẬP CÂU HỎI TỪ WORD";

        return new ExamTemplate(title, noteList.ToArray(), sectionList, guideList.ToArray());
    }

    /// <summary>Bộ cột, dòng ví dụ và hướng dẫn của file mẫu bài học — dùng chung cho Excel và Word.</summary>
    private static (string[] Header, string[][] Sample, (string, string)[] Guide) LessonTemplate() =>
    (
        new[]
        {
            "Tiêu đề bài học (*)", "Loại nội dung", "Thời lượng (phút)", "Thứ tự",
            "Đường dẫn tài nguyên", "Nội dung (HTML)", "Hiển thị"
        },
        new[]
        {
            new[]
            {
                "Bài 1 - Tổng quan an toàn thông tin", "Văn bản", "20", "1", "",
                "<h3>Mục tiêu</h3><p>Hiểu các nguy cơ bảo mật phổ biến trong doanh nghiệp.</p>", "Có"
            },
            new[]
            {
                "Bài 2 - Video hướng dẫn nhận diện Phishing", "Video", "15", "2", "/uploads/lesson/202608/phishing.mp4",
                "<p>Xem video và ghi chú các dấu hiệu nhận biết.</p>", "Có"
            },
            new[]
            {
                "Bài 3 - Quy định bảo mật nội bộ", "Tài liệu", "25", "3", "/uploads/lesson/202608/quy-dinh.pdf",
                "<p>Đọc kỹ tài liệu đính kèm trước khi làm bài kiểm tra.</p>", "Có"
            }
        },
        new (string, string)[]
        {
            ("Tiêu đề bài học (*)", "Bắt buộc."),
            ("Loại nội dung", "Văn bản | Tài liệu | Video | Hình ảnh | Liên kết. Bỏ trống mặc định là Văn bản."),
            ("Thời lượng (phút)", "Số nguyên. Dùng để tính tổng thời lượng khóa học."),
            ("Thứ tự", "Vị trí trong khóa học. Bỏ trống hoặc 0 thì hệ thống tự đánh số tiếp theo."),
            ("Đường dẫn tài nguyên", "Đường dẫn file ĐÃ tải lên hệ thống, ví dụ /uploads/lesson/202608/tai-lieu.pdf"),
            ("", "File mẫu KHÔNG nhúng được tài nguyên. Hãy tải file lên trước bằng nút Tệp đính kèm rồi sao chép đường dẫn vào đây."),
            ("Nội dung (HTML)", "Hỗ trợ thẻ HTML: <h3>, <p>, <ul>, <b>, <img>..."),
            ("Hiển thị", "Có | Không. Bỏ trống mặc định là Có."),
            ("", ""),
            ("LƯU Ý", "Xóa 3 dòng ví dụ trước khi nhập dữ liệu thật.")
        }
    );

    public byte[] BuildLessonTemplate(ImportFormat format = ImportFormat.Excel)
    {
        var (header, sample, guide) = LessonTemplate();
        var template = new ImportTemplate("FILE MẪU NHẬP BÀI HỌC", "BaiHoc", header, sample, guide,
            new double[] { 44, 18, 17, 9, 40, 60, 11 });
        return format == ImportFormat.Word ? _templateWriter.BuildWord(template) : _templateWriter.BuildExcel(template);
    }

    /// <summary>Chỉ có bản Excel.</summary>
    public byte[] BuildDepartmentTemplate() => _templateWriter.BuildExcel(new ImportTemplate(
        "FILE MẪU NHẬP PHÒNG BAN",
        "PhongBan",
        new[]
        { "Tên phòng ban (*)", "Là phòng Nhân sự", "Đang hoạt động" },
        new[]
        {
            new[] { "Phòng Kỹ thuật thi công", "", "x" },
            new[] { "Phòng Nhân sự", "x", "x" },
            new[] { "Phòng IT", "", "x" },
        },
        new (string, string)[]
        {
            ("Tên phòng ban (*)", "Bắt buộc. Trùng tên phòng ban đã có thì dòng đó bị bỏ qua."),
            ("Là phòng Nhân sự", "Ghi x nếu đây là phòng Nhân sự (duyệt xuất bản cuối cùng). Toàn hệ thống chỉ có ĐÚNG MỘT phòng như vậy."),
            ("Đang hoạt động", "Ghi x để bật. Bỏ trống cũng được coi là đang hoạt động."),
            ("", ""),
            ("MÃ PHÒNG BAN", "Không có cột mã — hệ thống tự sinh từ tên (VD \"Phòng Kỹ thuật thi công\" -> KTTC)."),
            ("LƯU Ý", "Xóa 3 dòng ví dụ trước khi nhập dữ liệu thật."),
            ("", "Dòng nào sai sẽ bị bỏ qua và báo lỗi kèm số dòng, các dòng còn lại vẫn được nhập."),
        },
        new double[] { 40, 18, 16 }));

    /// <summary>Chỉ có bản Excel.</summary>
    public byte[] BuildPositionTemplate() => _templateWriter.BuildExcel(new ImportTemplate(
        "FILE MẪU NHẬP VỊ TRÍ CÔNG VIỆC",
        "ViTri",
        new[]
        { "Tên vị trí công việc (*)", "Phòng ban", "Đang hoạt động" },
        new[]
        {
            new[] { "Kỹ sư giám sát", "Phòng Kỹ thuật thi công", "x" },
            new[] { "Chuyên viên tuyển dụng", "Phòng Nhân sự", "x" },
            new[] { "Nhân viên vận hành", "", "x" },
        },
        new (string, string)[]
        {
            ("Tên vị trí công việc (*)", "Bắt buộc. Trùng tên trong CÙNG phòng ban thì dòng đó bị bỏ qua."),
            ("Phòng ban", "Ghi tên hoặc mã phòng ban đã có. Bỏ trống = vị trí dùng chung, không thuộc phòng nào."),
            ("Đang hoạt động", "Ghi x để bật. Bỏ trống cũng được coi là đang hoạt động."),
            ("", ""),
            ("MÃ VỊ TRÍ", "Không có cột mã — hệ thống tự sinh theo mã phòng ban (VD KTTC-01)."),
            ("LƯU Ý", "Phòng ban phải tạo TRƯỚC khi nhập vị trí, nếu không dòng đó báo lỗi không tìm thấy phòng ban."),
            ("", "Xóa 3 dòng ví dụ trước khi nhập dữ liệu thật."),
        },
        new double[] { 34, 34, 16 }));

    /// <summary>Chỉ có bản Excel.</summary>
    public byte[] BuildUserTemplate() => _templateWriter.BuildExcel(new ImportTemplate(
        "FILE MẪU NHẬP NGƯỜI DÙNG",
        "NguoiDung",
        new[]
        {
                "Họ và tên (*)", "Email (*)", "Mật khẩu (*)", "Vai trò (*)",
                "Số điện thoại", "Phòng ban", "Vị trí công việc", "Đang hoạt động"
        },
        new[]
        {
            new[] { "Nguyễn Văn An", "an.nv@congty.vn", "Hbe@2026", "Học viên", "0901234567", "Phòng Kỹ thuật thi công", "Kỹ sư giám sát", "x" },
            new[] { "Trần Thị Bình", "binh.tt@congty.vn", "Hbe@2026", "Trưởng Nhóm", "0912345678", "Phòng Nhân sự", "Chuyên viên tuyển dụng", "x" },
            new[] { "Lê Văn Cường", "cuong.lv@congty.vn", "Hbe@2026", "Trưởng phòng", "", "Phòng IT", "", "x" },
        },
        new (string, string)[]
        {
            ("Họ và tên (*)", "Bắt buộc."),
            ("Email (*)", "Bắt buộc, dùng để đăng nhập. Email đã có trong hệ thống thì dòng đó bị bỏ qua."),
            ("Mật khẩu (*)", "Bắt buộc, tối thiểu 6 ký tự. Nên đặt chung một mật khẩu tạm rồi yêu cầu nhân viên tự đổi sau lần đăng nhập đầu."),
            ("Vai trò (*)", "Ghi đúng tên vai trò đang có trong màn hình \"Vai trò & Phân quyền\" (VD Học viên, Trưởng Nhóm, Trưởng phòng)."),
            ("Số điện thoại", "Không bắt buộc."),
            ("Phòng ban", "Ghi tên hoặc mã phòng ban đã có. Bỏ trống = chưa xếp phòng."),
            ("Vị trí công việc", "Ghi tên hoặc mã vị trí đã có. Bỏ trống = chưa xếp vị trí."),
            ("Đang hoạt động", "Ghi x để bật. Bỏ trống cũng được coi là đang hoạt động."),
            ("", ""),
            ("MÃ NHÂN VIÊN", "Không có cột mã — hệ thống tự sinh theo thứ tự (NV001, NV002...)."),
            ("LƯU Ý", "Phòng ban và vị trí phải tạo TRƯỚC khi nhập người dùng."),
            ("", "Xóa 3 dòng ví dụ trước khi nhập dữ liệu thật."),
        },
        new double[] { 26, 30, 16, 18, 16, 30, 26, 16 }));

    // ==================================================================
    //  NHẬP LIỆU
    // ==================================================================

    public async Task<ImportResultDto> ImportQuestionAsync(int quizId, Stream stream, ImportFormat format)
    {
        var errorList = new List<ImportErrorDto>();
        var newList = new List<Question>();

        _ = await _db.Quiz.FirstOrDefaultAsync(x => x.Id == quizId)
            ?? throw new KeyNotFoundException("Không tìm thấy bài kiểm tra.");

        var nextOrder = (await _db.Question.Where(x => x.QuizId == quizId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0) + 1;

        // Excel / Word / PDF đều quy về cùng một danh sách dòng đã tách ô (xem IImportFileReader), nên toàn
        // bộ quy tắc kiểm tra bên dưới chỉ viết một lần cho cả ba định dạng.
        var rowList = _fileReader.Read(stream, format);
        var totalRow = rowList.Count;

        foreach (var row in rowList)
        {
            var (parsed, error) = ParseQuestionRow(row, 0);
            if (parsed is null)
            {
                errorList.Add(new ImportErrorDto(row.Number, error!));
                continue;
            }

            var question = new Question
            {
                QuizId = quizId,
                Content = parsed.Content,
                QuestionType = parsed.DeclaredType ?? QuestionType.SingleChoice,
                Score = parsed.Score is > 0 ? parsed.Score.Value : 1m,
                OrderIndex = nextOrder++,
                Explanation = parsed.Explanation
            };

            var order = 1;
            foreach (var answer in parsed.AnswerList)
            {
                question.AnswerList.Add(new Answer
                {
                    Content = answer.Content,
                    IsCorrect = answer.IsCorrect,
                    OrderIndex = order++
                });
            }

            newList.Add(question);
        }

        if (newList.Count > 0)
        {
            _db.Question.AddRange(newList);
            await _db.SaveChangesAsync();
        }

        return new ImportResultDto(totalRow, newList.Count, errorList.Count, errorList);
    }

    /// <summary>
    /// Đọc một bảng câu hỏi soạn theo file mẫu CẢ KHÓA HỌC (xem <see cref="BuildQuestionSheetTemplate"/>) và
    /// gom các dòng lại theo cột "Bài kiểm tra" ở đầu.
    ///
    /// Chỉ đọc và gom nhóm — không đụng tới database. Việc ghép từng nhóm vào đúng bài kiểm tra và ghi câu
    /// hỏi nằm ở ImportService, dùng chung với lối nhập file Word soạn tự do.
    ///
    /// Các dòng cùng tên bài được gom về một nhóm dù nằm rải rác trong file (người điền hay chèn thêm câu vào
    /// giữa bảng), thứ tự nhóm theo lần xuất hiện đầu tiên. Dòng bỏ trống cột tên bài rơi vào nhóm không tên —
    /// ImportService đặt cho nó tên file làm tên bài kiểm tra.
    /// </summary>
    public QuestionSheet ReadQuestionSheet(Stream stream, ImportFormat format)
    {
        var rowList = _fileReader.Read(stream, format);
        var errorList = new List<(int Line, string Message)>();

        // Khóa gom nhóm chỉ bỏ hoa thường và khoảng trắng thừa; so khớp sâu hơn (bỏ dấu tiếng Việt, khớp theo
        // số bài) là việc của ImportService khi đối chiếu với bài kiểm tra / bài học đã có.
        var groupList = new List<(string Key, string? Title, List<ParsedQuestion> QuestionList)>();

        foreach (var row in rowList)
        {
            var (parsed, error) = ParseQuestionRow(row, 1);
            if (parsed is null)
            {
                errorList.Add((row.Number, error!));
                continue;
            }

            var title = row.Text(1);
            var key = title.ToLowerInvariant();

            var group = groupList.FirstOrDefault(x => x.Key == key);
            if (group.QuestionList is null)
            {
                group = (key, NullIfEmpty(title), new List<ParsedQuestion>());
                groupList.Add(group);
            }

            group.QuestionList.Add(parsed);
        }

        return new QuestionSheet(
            groupList.Select(x => new ParsedQuestionGroup(x.Title, x.QuestionList)).ToList(),
            errorList,
            rowList.Count);
    }

    /// <summary>
    /// Quy tắc đọc MỘT dòng của bảng câu hỏi — một bản duy nhất dùng cho cả file mẫu của một bài kiểm tra lẫn
    /// file mẫu cả khóa học, để hai lối nhập không bao giờ chấp nhận những thứ khác nhau.
    /// </summary>
    /// <param name="columnOffset">Số cột chèn thêm phía trước bộ cột chuẩn: 0 cho file mẫu một bài kiểm tra,
    /// 1 cho file mẫu cả khóa học (có thêm cột "Bài kiểm tra" ở đầu).</param>
    /// <returns>Câu hỏi đọc được, hoặc (null, lý do bỏ qua dòng này).</returns>
    private static (ParsedQuestion? Question, string? Error) ParseQuestionRow(ImportRow row, int columnOffset)
    {
        var content = row.Text(columnOffset + 1);
        if (string.IsNullOrWhiteSpace(content))
            return (null, "Thiếu nội dung câu hỏi.");

        // Đọc các đáp án ở 5 cột liền nhau (A → E).
        var answerTextList = new List<(string Letter, string Content)>();
        for (var i = 0; i < MaxAnswerColumn; i++)
        {
            var value = row.Text(columnOffset + 4 + i);
            if (!string.IsNullOrWhiteSpace(value))
                answerTextList.Add((AnswerLetter[i], value.Trim()));
        }

        if (answerTextList.Count < 2)
            return (null, "Phải có ít nhất 2 đáp án (cột Đáp án A và B).");

        // Đáp án đúng: "A" hoặc "A,C"
        var correctLetterList = row.Text(columnOffset + 9)
            .Split(new[] { ',', ';', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .ToList();

        if (correctLetterList.Count == 0)
            return (null, "Thiếu cột 'Đáp án đúng'. Ghi chữ cái, ví dụ: A hoặc A,C");

        var invalid = correctLetterList.FirstOrDefault(x => answerTextList.All(a => a.Letter != x));
        if (invalid is not null)
            return (null, $"Đáp án đúng '{invalid}' không tồn tại hoặc cột đó đang để trống.");

        var questionType = ParseQuestionType(row.Text(columnOffset + 2), correctLetterList.Count);

        if (questionType != QuestionType.MultipleChoice && correctLetterList.Count > 1)
            return (null, "Câu hỏi một lựa chọn chỉ được có duy nhất 1 đáp án đúng.");
        if (questionType == QuestionType.TrueFalse && answerTextList.Count != 2)
            return (null, "Câu hỏi Đúng/Sai phải có đúng 2 đáp án.");

        var scoreValue = row.Int(columnOffset + 3);

        var answerList = answerTextList
            .Select(x => new ParsedAnswer(x.Content, correctLetterList.Contains(x.Letter)))
            .ToList();

        return (new ParsedQuestion(
            content.Trim(), answerList, NullIfEmpty(row.Text(columnOffset + 10)),
            scoreValue > 0 ? scoreValue : null, row.Number, false, questionType), null);
    }

    public async Task<ImportResultDto> ImportLessonAsync(int courseId, Stream stream, ImportFormat format)
    {
        var errorList = new List<ImportErrorDto>();
        var newList = new List<Lesson>();

        _ = await _db.Course.FirstOrDefaultAsync(x => x.Id == courseId)
            ?? throw new KeyNotFoundException("Không tìm thấy khóa học.");

        var nextOrder = (await _db.Lesson.Where(x => x.CourseId == courseId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0) + 1;

        var rowList = _fileReader.Read(stream, format);
        var totalRow = rowList.Count;

        foreach (var row in rowList)
        {
            var rowNumber = row.Number;
            var title = row.Text(1);
            if (string.IsNullOrWhiteSpace(title))
            {
                errorList.Add(new ImportErrorDto(rowNumber, "Thiếu tiêu đề bài học."));
                continue;
            }

            var order = row.Int(4);
            var visible = row.Text(7).Trim().ToLowerInvariant();

            newList.Add(new Lesson
            {
                CourseId = courseId,
                Title = title.Trim(),
                ContentType = ParseContentType(row.Text(2)),
                DurationMinutes = row.Int(3),
                OrderIndex = order > 0 ? order : nextOrder++,
                ResourceUrl = NullIfEmpty(row.Text(5)),
                Content = NullIfEmpty(row.Text(6)),
                IsActive = visible is not ("không" or "khong" or "no" or "false" or "0")
            });
        }

        if (newList.Count > 0)
        {
            _db.Lesson.AddRange(newList);
            await _db.SaveChangesAsync();
        }

        return new ImportResultDto(totalRow, newList.Count, errorList.Count, errorList);
    }

    public async Task<ImportResultDto> ImportDepartmentAsync(Stream stream)
    {
        var rowList = _fileReader.Read(stream, ImportFormat.Excel);
        var totalRow = rowList.Count;
        var errorList = new List<ImportErrorDto>();

        // Nạp sẵn tên đã có để so trong bộ nhớ, và cộng dồn tên vừa thêm trong CHÍNH file này — nếu không,
        // hai dòng trùng tên trong cùng một file đều lọt qua và tạo ra hai phòng ban giống hệt nhau.
        var existingNameSet = (await _db.Department.Select(x => x.Name).ToListAsync())
            .Select(x => x.ToLowerInvariant()).ToHashSet();
        var existingCodeSet = (await _db.Department.Select(x => x.Code).ToListAsync()).ToHashSet();
        var hasHrDepartment = await _db.Department.AnyAsync(x => x.IsHrDepartment);

        var addedList = new List<Department>();

        foreach (var row in rowList)
        {
            var rowNumber = row.Number;
            var name = row.Text(1);
            if (name.Length == 0) { errorList.Add(new ImportErrorDto(rowNumber, "Thiếu tên phòng ban.")); continue; }
            if (name.Length > 150) { errorList.Add(new ImportErrorDto(rowNumber, "Tên phòng ban dài quá 150 ký tự.")); continue; }
            if (!existingNameSet.Add(name.ToLowerInvariant()))
            {
                errorList.Add(new ImportErrorDto(rowNumber, $"Phòng ban \"{name}\" đã có trong hệ thống."));
                continue;
            }

            var isHr = IsChecked(row.Text(2));
            if (isHr && hasHrDepartment)
            {
                // Không tự ý gỡ cờ của phòng cũ: đó là quyết định về quy trình duyệt, không phải việc mà một
                // lần nhập Excel được phép thay đổi ngầm.
                errorList.Add(new ImportErrorDto(rowNumber,
                    "Hệ thống đã có phòng Nhân sự. Bỏ dấu x ở cột \"Là phòng Nhân sự\", hoặc đổi phòng Nhân sự hiện tại trước."));
                continue;
            }
            if (isHr) hasHrDepartment = true;

            addedList.Add(new Department
            {
                Code = NextOrgCode(existingCodeSet, OrgCodeHelper.InitialsFromName(name) is { Length: > 0 } baseCode ? baseCode : "PB"),
                Name = name,
                IsActive = IsCheckedOrBlank(row.Text(3)),
                IsHrDepartment = isHr
            });
        }

        if (addedList.Count > 0)
        {
            _db.Department.AddRange(addedList);
            await _db.SaveChangesAsync();
        }

        return new ImportResultDto(totalRow, addedList.Count, errorList.Count, errorList);
    }

    public async Task<ImportResultDto> ImportPositionAsync(Stream stream)
    {
        var rowList = _fileReader.Read(stream, ImportFormat.Excel);
        var totalRow = rowList.Count;
        var errorList = new List<ImportErrorDto>();

        var departmentList = await _db.Department.AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.Code }).ToListAsync();

        var existingKeySet = (await _db.Position.Select(x => new { x.Name, x.DepartmentId }).ToListAsync())
            .Select(x => PositionKey(x.Name, x.DepartmentId)).ToHashSet();
        var existingCodeSet = (await _db.Position.Select(x => x.Code).ToListAsync()).ToHashSet();

        var addedList = new List<Position>();

        foreach (var row in rowList)
        {
            var rowNumber = row.Number;
            var name = row.Text(1);
            if (name.Length == 0) { errorList.Add(new ImportErrorDto(rowNumber, "Thiếu tên vị trí công việc.")); continue; }
            if (name.Length > 150) { errorList.Add(new ImportErrorDto(rowNumber, "Tên vị trí dài quá 150 ký tự.")); continue; }

            int? departmentId = null;
            var departmentText = row.Text(2);
            if (departmentText.Length > 0)
            {
                var department = departmentList.FirstOrDefault(x =>
                    string.Equals(x.Name, departmentText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Code, departmentText, StringComparison.OrdinalIgnoreCase));
                if (department is null)
                {
                    errorList.Add(new ImportErrorDto(rowNumber, $"Không tìm thấy phòng ban \"{departmentText}\". Hãy tạo phòng ban trước."));
                    continue;
                }
                departmentId = department.Id;
            }

            if (!existingKeySet.Add(PositionKey(name, departmentId)))
            {
                errorList.Add(new ImportErrorDto(rowNumber, $"Vị trí \"{name}\" đã có trong phòng ban này."));
                continue;
            }

            var prefix = departmentId is null
                ? "VT"
                : departmentList.First(x => x.Id == departmentId).Code;

            addedList.Add(new Position
            {
                Code = NextPositionCode(existingCodeSet, prefix),
                Name = name,
                DepartmentId = departmentId,
                IsActive = IsCheckedOrBlank(row.Text(3))
            });
        }

        if (addedList.Count > 0)
        {
            _db.Position.AddRange(addedList);
            await _db.SaveChangesAsync();
        }

        return new ImportResultDto(totalRow, addedList.Count, errorList.Count, errorList);
    }

    public async Task<ImportResultDto> ImportUserAsync(Stream stream)
    {
        var rowList = _fileReader.Read(stream, ImportFormat.Excel);
        var totalRow = rowList.Count;
        var errorList = new List<ImportErrorDto>();

        var roleList = await _db.Role.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Name }).ToListAsync();
        var departmentList = await _db.Department.AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.Code }).ToListAsync();
        var positionList = await _db.Position.AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.Code, x.DepartmentId }).ToListAsync();

        var existingEmailSet = (await _db.User.Select(x => x.Email).ToListAsync())
            .Select(x => x.ToLowerInvariant()).ToHashSet();

        // Mã nhân viên cấp liên tiếp trong cùng lần nhập: hỏi CSDL cho từng dòng thì mọi dòng đều nhận cùng
        // một số vì chưa dòng nào được lưu.
        var nextEmployeeNumber = (await _db.User
                .Where(x => x.EmployeeCode.StartsWith("NV"))
                .Select(x => x.EmployeeCode.Substring(2))
                .ToListAsync())
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var addedList = new List<User>();

        foreach (var row in rowList)
        {
            var rowNumber = row.Number;
            var fullName = row.Text(1);
            var email = row.Text(2).ToLowerInvariant();
            var password = row.Text(3);
            var roleText = row.Text(4);

            if (fullName.Length == 0) { errorList.Add(new ImportErrorDto(rowNumber, "Thiếu họ và tên.")); continue; }
            if (email.Length == 0) { errorList.Add(new ImportErrorDto(rowNumber, "Thiếu email.")); continue; }
            if (!email.Contains('@') || email.Contains(' '))
            {
                errorList.Add(new ImportErrorDto(rowNumber, $"Email \"{email}\" không hợp lệ."));
                continue;
            }
            if (!existingEmailSet.Add(email))
            {
                errorList.Add(new ImportErrorDto(rowNumber, $"Email \"{email}\" đã có tài khoản."));
                continue;
            }
            if (password.Length < 6)
            {
                errorList.Add(new ImportErrorDto(rowNumber, "Mật khẩu phải có tối thiểu 6 ký tự."));
                continue;
            }

            var role = roleList.FirstOrDefault(x => string.Equals(x.Name, roleText, StringComparison.OrdinalIgnoreCase));
            if (role is null)
            {
                errorList.Add(new ImportErrorDto(rowNumber,
                    roleText.Length == 0 ? "Thiếu vai trò." : $"Không tìm thấy vai trò \"{roleText}\"."));
                continue;
            }

            int? departmentId = null;
            var departmentText = row.Text(6);
            if (departmentText.Length > 0)
            {
                var department = departmentList.FirstOrDefault(x =>
                    string.Equals(x.Name, departmentText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Code, departmentText, StringComparison.OrdinalIgnoreCase));
                if (department is null)
                {
                    errorList.Add(new ImportErrorDto(rowNumber, $"Không tìm thấy phòng ban \"{departmentText}\"."));
                    continue;
                }
                departmentId = department.Id;
            }

            int? positionId = null;
            var positionText = row.Text(7);
            if (positionText.Length > 0)
            {
                // Ưu tiên vị trí thuộc đúng phòng ban vừa xác định: nhiều phòng có thể cùng đặt tên vị trí
                // "Chuyên viên", chọn bừa cái đầu tiên là xếp nhân viên sang phòng khác.
                var candidateList = positionList.Where(x =>
                    string.Equals(x.Name, positionText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Code, positionText, StringComparison.OrdinalIgnoreCase)).ToList();

                var position = candidateList.FirstOrDefault(x => x.DepartmentId == departmentId)
                            ?? candidateList.FirstOrDefault();
                if (position is null)
                {
                    errorList.Add(new ImportErrorDto(rowNumber, $"Không tìm thấy vị trí công việc \"{positionText}\"."));
                    continue;
                }
                positionId = position.Id;
            }

            addedList.Add(new User
            {
                EmployeeCode = $"NV{nextEmployeeNumber++:D3}",
                FullName = fullName,
                Email = email,
                PasswordHash = _passwordHasher.Hash(password),
                RoleId = role.Id,
                DepartmentId = departmentId,
                PositionId = positionId,
                PhoneNumber = NullIfEmpty(row.Text(5)),
                IsActive = IsCheckedOrBlank(row.Text(8))
            });
        }

        if (addedList.Count > 0)
        {
            _db.User.AddRange(addedList);
            await _db.SaveChangesAsync();
        }

        return new ImportResultDto(totalRow, addedList.Count, errorList.Count, errorList);
    }

    // ==================================================================
    //  HÀM DÙNG CHUNG
    // ==================================================================

    /// <summary>Ô đánh dấu trong Excel: nhận x / X / v / 1 / true / có. Người điền mỗi nơi một kiểu, bắt
    /// đúng một ký tự thì phần lớn file thật sẽ bị hiểu là "không".</summary>
    private static bool IsChecked(string raw) =>
        raw.Trim().ToLowerInvariant() is "x" or "v" or "1" or "true" or "yes" or "có" or "co";

    /// <summary>Như trên nhưng ô TRỐNG cũng tính là bật — cột "Đang hoạt động" bỏ trống thì ý người dùng gần
    /// như chắc chắn là "đang hoạt động", không phải "khóa ngay từ đầu".</summary>
    private static bool IsCheckedOrBlank(string raw) => raw.Trim().Length == 0 || IsChecked(raw);

    private static string PositionKey(string name, int? departmentId) =>
        name.Trim().ToLowerInvariant() + "" + (departmentId?.ToString() ?? "");

    /// <summary>Mã phòng ban chưa bị trùng, đếm thêm hậu tố khi cần. Tập mã được cập nhật ngay để các dòng
    /// sau trong cùng file không cấp lại đúng mã đó.</summary>
    private static string NextOrgCode(HashSet<string> usedCodeSet, string baseCode)
    {
        var code = baseCode;
        var suffix = 1;
        while (!usedCodeSet.Add(code))
        {
            suffix++;
            code = $"{baseCode}{suffix}";
        }
        return code;
    }

    private static string NextPositionCode(HashSet<string> usedCodeSet, string prefix)
    {
        var seq = usedCodeSet.Count(x => x.StartsWith(prefix + "-")) + 1;
        string code;
        do { code = $"{prefix}-{seq++:D2}"; } while (!usedCodeSet.Add(code));
        return code;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static QuestionType ParseQuestionType(string raw, int correctCount)
    {
        var value = raw.Trim().ToLowerInvariant();

        if (value.Contains("nhiều") || value.Contains("nhieu") || value.Contains("multiple") || value == "2")
            return QuestionType.MultipleChoice;

        if (value.Contains("đúng/sai") || value.Contains("dung/sai") || value.Contains("truefalse")
            || value.Contains("true/false") || value == "3")
            return QuestionType.TrueFalse;

        if (value.Contains("một") || value.Contains("mot") || value.Contains("single") || value == "1")
            return QuestionType.SingleChoice;

        // Bỏ trống -> tự nhận theo số đáp án đúng
        return correctCount > 1 ? QuestionType.MultipleChoice : QuestionType.SingleChoice;
    }

    private static LessonContentType ParseContentType(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();
        if (value.Contains("video")) return LessonContentType.Video;
        if (value.Contains("tài liệu") || value.Contains("tai lieu") || value.Contains("document") || value.Contains("pdf"))
            return LessonContentType.Document;
        if (value.Contains("hình") || value.Contains("hinh") || value.Contains("image") || value.Contains("ảnh"))
            return LessonContentType.Image;
        if (value.Contains("liên kết") || value.Contains("lien ket") || value.Contains("link") || value.Contains("url"))
            return LessonContentType.Link;
        return LessonContentType.Text;
    }
}
