// File: Application/Features/AiDocuments/IAiDocumentService.cs
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.AiDocuments;

/// <summary>Các ca sử dụng của chức năng AiDocument. Tầng API gọi qua cổng này (xem API/Controllers/AiDocumentService.cs).</summary>
public interface IAiDocumentService
{
    /// <summary>Ollama đã chạy và đã cài model chưa.</summary>
    Task<Result> GetStatus(CancellationToken cancellationToken);

    /// <summary>Danh sách tài liệu đã nạp, gộp các đoạn theo tên file.</summary>
    Task<Result> GetList(int? courseId, CancellationToken cancellationToken);

    /// <summary>Xem các đoạn đã cắt của một tài liệu — để quản trị viên kiểm tra chất lượng bóc tách.</summary>
    Task<Result> GetChunkList(string sourceFileName, int? courseId, CancellationToken cancellationToken);

    /// <summary>Tải một tài liệu lên và nạp ngay vào kho tri thức. Tải lại tài liệu trùng tên sẽ THAY THẾ
    /// bản cũ (xóa đoạn cũ + file cũ) chứ không nhân đôi dữ liệu.</summary>
    Task<Result> Upload(FileUpload file, int? courseId, int? lessonId, CancellationToken cancellationToken);

    /// <summary>Thử tìm kiếm ngữ nghĩa để kiểm tra kho tri thức trả lời có đúng không.</summary>
    Task<Result> Search(AiDocumentSearchRequest request, CancellationToken cancellationToken);

    /// <summary>Hỏi đáp trên tài liệu đã nạp: tìm các đoạn liên quan rồi để model ngôn ngữ nội bộ viết câu trả lời.
    /// Trả về dạng SSE (text/event-stream) vì model 7B chạy trên CPU chỉ sinh được vài chữ mỗi giây —
    /// chờ trả lời trọn vẹn rồi mới hiện thì người dùng ngồi nhìn màn hình trắng cả phút.</summary>
    Task Ask(AiDocumentAskRequest request, IEventStream events, CancellationToken cancellationToken);

    /// <summary>AI đã học tới đâu trong các khóa học nhân viên đang được học.</summary>
    Task<Result> GetCourseKnowledge(CancellationToken cancellationToken);

    /// <summary>Bắt AI học lại nội dung khóa học ngay, không chờ lượt đồng bộ định kỳ. Dùng khi vừa xuất bản
    /// một khóa học và muốn HB Minerva trả lời được về nó trong vài phút tới thay vì chờ tới lượt quét sau.
    ///
    /// <paramref name="isForced"/> = true thì nạp lại TẤT CẢ, kể cả bài học không đổi chữ nào — chỉ dùng sau
    /// khi đổi model nhúng hoặc khi nghi kho tri thức đã hỏng, vì nó bắt Ollama chạy lại từ đầu.</summary>
    Task<Result> SyncCourseKnowledge(bool isForced, int? courseId, CancellationToken cancellationToken);

    /// <summary>Xóa một tài liệu khỏi kho tri thức (xóa cả các đoạn lẫn file gốc).</summary>
    Task<Result> Delete(string sourceFileName, int? courseId, CancellationToken cancellationToken);
}
