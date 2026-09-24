// File: Application/Features/Translations/ITranslationService.cs
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.Translations;

/// <summary>Các ca sử dụng của chức năng Translation. Tầng API gọi qua cổng này (xem API/Controllers/TranslationService.cs).</summary>
public interface ITranslationService
{
    /// <summary>Từ điển phẳng { khóa: câu chữ } của một ngôn ngữ — thứ mà wwwroot/js/language.js tải về.
    ///
    /// KHÔNG yêu cầu đăng nhập, vì trang đăng nhập và trang chủ cũng cần dịch mà lúc đó chưa có token nào.
    /// Ở đây không có gì bí mật: toàn bộ nội dung này vốn đã nằm sẵn trong wwwroot/locales/*.json mà trình
    /// duyệt tải công khai từ trước tới nay.</summary>
    Task<Result> GetDictionary(string lang, CancellationToken cancellationToken);

    /// <summary>Các ngôn ngữ giao diện, kèm số khóa đã có câu chữ của từng ngôn ngữ.
    ///
    /// KHÔNG đòi đăng nhập, cùng lý do với <see cref="GetDictionary"/>: nút chuyển ngôn ngữ có mặt ở cả trang
    /// đăng nhập lẫn trang chủ, mà lúc đó chưa có token nào.</summary>
    Task<Result> GetLanguageList(CancellationToken cancellationToken);

    /// <summary>Thêm một ngôn ngữ giao diện mới.</summary>
    Task<Result> CreateLanguage(LanguageCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Sửa tên hiển thị / thẻ locale / bật tắt một ngôn ngữ.</summary>
    Task<Result> UpdateLanguage(string code, LanguageUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>Xóa một ngôn ngữ do công ty tự thêm, kèm toàn bộ câu chữ đã dịch cho nó.</summary>
    Task<Result> DeleteLanguage(string code, CancellationToken cancellationToken);

    /// <summary>Các nhóm khóa (tiền tố trước dấu chấm) để lọc theo màn hình.</summary>
    Task<Result> GetGroupList(CancellationToken cancellationToken);

    /// <summary>Danh sách khóa dịch, mỗi dòng gồm câu chữ của tất cả ngôn ngữ.</summary>
    /// <param name="source">"system" = khóa đi kèm phần mềm, "custom" = khóa công ty tự thêm,
    /// "customized" = khóa hệ thống đã bị sửa khác bản gốc. Bỏ trống = tất cả.</param>
    Task<Result> GetList(string? keyword, string? group, string? source, int page = 1, int pageSize = 30, CancellationToken cancellationToken = default);

    /// <summary>Thêm một khóa dịch của riêng công ty (thuật ngữ chuyên ngành chưa có trong bản xuất xưởng).</summary>
    Task<Result> Create(TranslationSaveRequest request, CancellationToken cancellationToken);

    /// <summary>Sửa câu chữ của một khóa đã có.</summary>
    Task<Result> Update(TranslationSaveRequest request, CancellationToken cancellationToken);

    /// <summary>Trả một khóa hệ thống về đúng câu chữ xuất xưởng.
    ///
    /// Dùng PUT chứ không POST là có chủ đích: đây là sửa dữ liệu sẵn có, và cơ chế phân quyền suy thao tác
    /// từ HTTP method (xem FunctionKey.ActionFromHttpMethod) — để POST thì thao tác này lại đòi quyền
    /// "thêm mới", sai với việc nó thực sự làm.</summary>
    Task<Result> Reset(TranslationResetRequest request, CancellationToken cancellationToken);

    /// <summary>Xóa một khóa do công ty tự thêm. Khóa hệ thống KHÔNG xóa được — mã nguồn đang gọi t() với
    /// khóa đó, xóa đi thì màn hình rơi về chuỗi dự phòng trong HTML mà không ai hiểu vì sao.</summary>
    Task<Result> Delete(string resourceKey, CancellationToken cancellationToken);

    /// <summary>Nhờ AI dịch một lô khóa từ ngôn ngữ nguồn sang ngôn ngữ đích.
    ///
    /// Dùng PUT chứ không POST là có chủ đích, cùng lý do với <see cref="Reset"/>: đây là ghi đè câu chữ của
    /// các dòng sẵn có, mà cơ chế phân quyền suy thao tác từ HTTP method (xem FunctionKey.ActionFromHttpMethod)
    /// — để POST thì việc này lại đòi quyền "thêm mới", sai với thứ nó thực sự làm.</summary>
    Task<Result> AiTranslate(AiTranslateRequest request, CancellationToken cancellationToken);
}
