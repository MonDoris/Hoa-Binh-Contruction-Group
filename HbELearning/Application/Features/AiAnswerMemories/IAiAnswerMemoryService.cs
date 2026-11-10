// File: Application/Features/AiAnswerMemories/IAiAnswerMemoryService.cs
namespace HbELearning.Application.Features.AiAnswerMemories;

/// <summary>Các ca sử dụng của chức năng AiAnswerMemory. Tầng API gọi qua cổng này (xem API/Controllers/AiDocuments/AiAnswerMemoryController.cs).</summary>
public interface IAiAnswerMemoryService
{
    /// <summary>Những câu HB Minerva đã ghi nhớ, câu được dùng lại nhiều nhất trước.</summary>
    Task<Result> GetList(string? keyword, CancellationToken cancellationToken);

    /// <summary>Sửa lại câu trả lời. Từ đó người hỏi lại nhận bản đã sửa thay vì nguyên văn của model.</summary>
    Task<Result> Update(int id, AiAnswerMemoryUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>Quên một câu: lần hỏi tới model sẽ viết lại từ đầu.</summary>
    Task<Result> Delete(int id, CancellationToken cancellationToken);

    /// <summary>Quên tất cả — dùng khi vừa thay hàng loạt quy định mà không muốn chờ từng câu cũ tự hết hiệu lực.</summary>
    Task<Result> DeleteAll(CancellationToken cancellationToken);
}
