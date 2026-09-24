// File: Application/DTOs/CommonDto.cs
namespace HbELearning.Application.DTOs;

public record PagedResult<T>(IReadOnlyList<T> Item, int TotalCount, int Page, int PageSize)
{
    public int TotalPage => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record MessageResponse(string Message, int? Id = null);
