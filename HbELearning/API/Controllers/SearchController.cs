// File: API/Controllers/SearchController.cs
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của ô tìm kiếm trên thanh trên. Mở cho mọi tài khoản đã đăng nhập; phạm vi khóa học mỗi
/// người thấy do <see cref="ISearchService"/> lọc theo quy tắc đối tượng của khóa học.</summary>
public class SearchController : BaseApiController
{
    private readonly ISearchService _service;

    public SearchController(ISearchService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search([FromQuery] string? keyword, CancellationToken cancellationToken)
        => ToActionResult(await _service.Search(keyword, cancellationToken));

    [HttpGet("person/{id:int}")]
    public async Task<ActionResult<SearchPersonDetailDto>> GetPerson(int id, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetPerson(id, cancellationToken));
}
