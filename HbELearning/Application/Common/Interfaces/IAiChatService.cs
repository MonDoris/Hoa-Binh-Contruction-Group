// File: Application/Common/Interfaces/IAiChatService.cs
// Cong (port) tach ra tu Infrastructure/Ai/OllamaChatService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IAiChatService
{
    /// <summary>Tên model sinh câu trả lời đang dùng (VD "qwen2.5:7b").</summary>
    string Model { get; }

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

    /// <summary>Bỏ bớt các đoạn tài liệu vượt quá trần chữ mà model đọc kịp trong thời gian chấp nhận được.
    ///
    /// Nơi gọi phải trim TRƯỚC rồi mới vừa gửi danh sách này cho <see cref="StreamAnswerAsync"/> vừa hiện nó
    /// lên làm bảng "AI đã dựa vào các đoạn sau". Hai thứ đó phải là MỘT: hiện cả những đoạn bị cắt khỏi câu
    /// lệnh là kê ra cho người dùng những đoạn model chưa từng đọc, rồi họ đi tìm xem câu trả lời lấy ý từ
    /// đâu trong đó mà không bao giờ thấy.</summary>
    List<DocumentChunkMatchDto> TrimToPromptBudget(IReadOnlyList<DocumentChunkMatchDto> contextList);
}
