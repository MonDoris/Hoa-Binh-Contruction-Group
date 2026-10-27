// File: Application/Features/Search/ISearchService.cs

namespace HbELearning.Application.Features.Search;

/// <summary>Ô tìm kiếm trên thanh trên: tìm người trong công ty và khóa học, xem hồ sơ một người.</summary>
public interface ISearchService
{
    /// <summary>Gợi ý nhanh khi đang gõ: vài người và vài khóa học khớp từ khóa.</summary>
    Task<Result> Search(string? keyword, CancellationToken cancellationToken);

    /// <summary>Hồ sơ một người kèm các khóa học họ soạn mà người đang xem được phép thấy.</summary>
    Task<Result> GetPerson(int id, CancellationToken cancellationToken);
}
