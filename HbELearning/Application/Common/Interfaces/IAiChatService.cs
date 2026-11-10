// File: Application/Common/Interfaces/IAiChatService.cs
// Cong (port) tach ra tu Infrastructure/Ai/OllamaChatService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IAiChatService
{
    /// <summary>Câu trả lời khi tài liệu nội bộ không có thông tin. Câu lệnh hệ thống dặn model viết đúng câu này,
    /// và mọi chỗ tự trả lời thay model (không tìm được đoạn tài liệu nào) cũng dùng đúng câu này — người hỏi
    /// không thấy hai kiểu từ chối khác nhau cho cùng một tình huống.
    ///
    /// Nói bằng ngôi "tôi" chứ không "Tài liệu nội bộ hiện tại...": người hỏi đang nói chuyện với HB Minerva, không
    /// biết và không cần biết phía sau có kho tài liệu nào.
    ///
    /// Chữ "Xin lỗi," ở đầu là có đo: thiếu nó thì qwen2.5:3b coi "Hiện tại tôi chưa có thông tin về phần này." như
    /// một câu mở/kết bài bình thường và chèn vào cả câu trả lời ĐÃ có thông tin (cuối bảng so sánh, trước giờ tan
    /// làm). Dặn thêm "trả lời được thì không viết câu này" trong câu lệnh cũng không chặn được.</summary>
    const string NoInformationAnswer = "Xin lỗi, hiện tại tôi chưa có thông tin về phần này.";

    /// <summary>Tên model sinh câu trả lời đang dùng (VD "qwen2.5:7b").</summary>
    string Model { get; }

    /// <summary>Phiên bản câu lệnh hỏi đáp. Câu trả lời HB Minerva đã ghi nhớ (xem AiAnswerMemory) chỉ được dùng lại
    /// khi được viết bởi đúng model VÀ đúng phiên bản câu lệnh này — sửa câu lệnh để chữa một lỗi trả lời thì những
    /// câu đã ghi nhớ theo câu lệnh cũ (có thể đang mang đúng lỗi đó) phải thôi được trả lại.</summary>
    string PromptVersion { get; }

    /// <summary>Đọc các đoạn tài liệu tìm được rồi viết câu trả lời, trả về TỪNG MẨU CHỮ ngay khi model sinh ra.
    /// Chạy trên CPU model 7B chỉ sinh được vài chữ mỗi giây, nên phải trả dần thay vì bắt người dùng
    /// ngồi chờ cả phút trước màn hình trắng.</summary>
    /// <para><paramref name="historyList"/> là vài lượt hỏi-đáp GẦN NHẤT của cùng một hội thoại, cũ trước
    /// mới sau. Truyền vào thì người dùng hỏi nối tiếp được ("còn nghỉ nửa ngày thì sao?"); để trống thì mỗi
    /// câu hỏi là một lần bắt đầu lại từ đầu như trước đây.</para>
    /// <para><paramref name="isCitationEnabled"/> quyết định model có chèn số nguồn [1] [2] vào câu trả lời
    /// hay không. Bật ở màn hình quản trị — nơi có bảng nguồn ngay bên cạnh để đối chiếu. TẮT ở trang của
    /// học viên: họ không được xem bảng nguồn, nên mấy con số đó chỉ là móc trỏ vào hư không.</para>
    IAsyncEnumerable<string> StreamAnswerAsync(
        string question, IReadOnlyList<DocumentChunkMatchDto> contextList,
        IReadOnlyList<AiChatTurnDto>? historyList = null, bool isCitationEnabled = true,
        CancellationToken cancellationToken = default);

    /// <summary>Tên model đọc được ảnh dùng để phân tích tệp đính kèm (VD "qwen3-vl:4b-instruct").</summary>
    string VisionModel { get; }

    /// <summary>Phân tích TỆP NGƯỜI DÙNG ĐÍNH KÈM rồi trả lời câu hỏi, từng mẩu chữ một như
    /// <see cref="StreamAnswerAsync"/>. Khác nhánh hỏi đáp tài liệu: ở đây không tra kho tri thức mà chỉ dựa vào
    /// chính những gì người dùng gửi — <paramref name="attachmentText"/> là chữ đã bóc từ PDF/Word/văn bản,
    /// <paramref name="imageList"/> là byte các ảnh (model đọc ảnh nhìn trực tiếp). Luôn dùng model đọc ảnh, kể cả
    /// khi chỉ có chữ, để không phải đổi model (và nạp lại) giữa hai kiểu câu hỏi.</summary>
    IAsyncEnumerable<string> StreamAttachmentAnswerAsync(
        string question, string attachmentText, IReadOnlyList<byte[]> imageList,
        IReadOnlyList<AiChatTurnDto>? historyList = null, CancellationToken cancellationToken = default);

    /// <summary>Bỏ bớt các đoạn tài liệu vượt quá trần chữ mà model đọc kịp trong thời gian chấp nhận được.
    ///
    /// Nơi gọi phải trim TRƯỚC rồi mới vừa gửi danh sách này cho <see cref="StreamAnswerAsync"/> vừa hiện nó
    /// lên làm bảng "AI đã dựa vào các đoạn sau". Hai thứ đó phải là MỘT: hiện cả những đoạn bị cắt khỏi câu
    /// lệnh là kê ra cho người dùng những đoạn model chưa từng đọc, rồi họ đi tìm xem câu trả lời lấy ý từ
    /// đâu trong đó mà không bao giờ thấy.</summary>
    List<DocumentChunkMatchDto> TrimToPromptBudget(IReadOnlyList<DocumentChunkMatchDto> contextList);
}
