// File: Application/Features/Translations/TranslationService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.Translations;

/// <summary>Bảng dịch giao diện: vừa phục vụ từ điển cho trang web đọc lúc tải, vừa là API của màn hình
/// "Dịch ngôn ngữ" để công ty tự sửa thuật ngữ chuyên ngành (xem <see cref="Translation"/>).</summary>
public class TranslationService : ITranslationService
{
    private const string CachePrefix = "translation-dictionary-";

    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IAiContentService _ai;
    private readonly ILogger<TranslationService> _logger;
    private readonly ICurrentUser _currentUser;

    public TranslationService(IAppDbContext db, IMemoryCache cache, IAiContentService ai, ILogger<TranslationService> logger, ICurrentUser currentUser)
    {
        _db = db;
        _cache = cache;
        _ai = ai;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<Result> GetDictionary(string lang, CancellationToken cancellationToken)
    {
        lang = (lang ?? "").Trim().ToLowerInvariant();
        if (lang.Length is 0 or > 10) return Result.Invalid(new MessageResponse("Mã ngôn ngữ không hợp lệ."));

        // Từ điển được đọc ở MỌI lần tải trang của MỌI người dùng nhưng gần như không bao giờ đổi — không
        // đệm thì mỗi lần mở một màn hình là một lượt quét cả nghìn dòng. Bộ đệm bị xóa ngay khi có ai lưu
        // bản dịch (xem ClearCache), nên không có chuyện sửa xong mà vẫn thấy chữ cũ.
        var cacheKey = CachePrefix + lang;
        if (_cache.TryGetValue<Dictionary<string, string>>(cacheKey, out var cached) && cached is not null)
            return Result.Success(cached);

        var dictionary = await _db.Translation.AsNoTracking()
            .Where(x => x.Lang == lang)
            .ToDictionaryAsync(x => x.ResourceKey, x => x.TranslatedText, cancellationToken);

        _cache.Set(cacheKey, dictionary, TimeSpan.FromHours(12));
        return Result.Success(dictionary);
    }

    public async Task<Result> GetLanguageList(CancellationToken cancellationToken)
    {
        var languageList = await _db.AppLanguage.AsNoTracking()
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var countByLang = (await _db.Translation.AsNoTracking()
            .GroupBy(x => x.Lang)
            .Select(g => new { Lang = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Lang, x => x.Count);

        return Result.Success(languageList.Select(x => new TranslationLanguageDto(
            x.Code, x.Name, countByLang.GetValueOrDefault(x.Code), x.LocaleTag, x.IsSystem, x.IsActive)));
    }

    public async Task<Result> CreateLanguage(LanguageCreateRequest request, CancellationToken cancellationToken)
    {
        var code = (request.Code ?? "").Trim().ToLowerInvariant();
        var name = (request.Name ?? "").Trim();

        // Mã ngôn ngữ đi thẳng vào đường dẫn API (/dictionary/{lang}) và vào localStorage của trình duyệt,
        // nên chỉ nhận đúng dạng thẻ ngôn ngữ BCP-47 rút gọn: "ja", "zh-hans", "pt-br".
        if (code.Length is < 2 or > 10 || !code.All(c => char.IsAsciiLetterLower(c) || c == '-'))
            return Result.Invalid(new MessageResponse("Mã ngôn ngữ chỉ gồm 2-10 chữ cái thường và dấu gạch ngang (VD \"ja\", \"zh-hans\")."));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            return Result.Invalid(new MessageResponse("Hãy nhập tên hiển thị của ngôn ngữ (tối đa 80 ký tự)."));

        if (await _db.AppLanguage.AnyAsync(x => x.Code == code, cancellationToken))
            return Result.Conflict(new MessageResponse($"Ngôn ngữ \"{code}\" đã có trong hệ thống."));

        var localeTag = string.IsNullOrWhiteSpace(request.LocaleTag) ? code : request.LocaleTag.Trim();
        if (localeTag.Length > 20)
            return Result.Invalid(new MessageResponse("Thẻ locale không được dài quá 20 ký tự."));

        var maxOrder = await _db.AppLanguage.AnyAsync(cancellationToken)
            ? await _db.AppLanguage.MaxAsync(x => x.OrderIndex, cancellationToken)
            : 0;

        _db.AppLanguage.Add(new AppLanguage
        {
            Code = code,
            Name = name,
            LocaleTag = localeTag,
            OrderIndex = maxOrder + 1,
            // Ngôn ngữ mới KHÔNG phải của hệ thống: nó không có file trong wwwroot/locales nên xóa đi là mất
            // hẳn, không "sống lại" ở lần khởi động sau như vi/en.
            IsSystem = false
        });

        // Chép sẵn câu chữ từ một ngôn ngữ có sẵn. Bắt đầu từ bảng rỗng thì mọi màn hình rơi về chuỗi dự
        // phòng trong HTML, người dịch không có dòng nào để mở ra sửa — chép trước rồi sửa dần dễ hơn nhiều.
        var copiedCount = 0;
        var copyFrom = (request.CopyFromLang ?? "").Trim().ToLowerInvariant();
        if (copyFrom.Length > 0)
        {
            var sourceList = await _db.Translation.AsNoTracking()
                .Where(x => x.Lang == copyFrom)
                .Select(x => new { x.ResourceKey, x.TranslatedText, x.IsSystem })
                .ToListAsync(cancellationToken);

            foreach (var source in sourceList)
            {
                _db.Translation.Add(new Translation
                {
                    ResourceKey = source.ResourceKey,
                    Lang = code,
                    TranslatedText = source.TranslatedText,
                    // Chưa có bản xuất xưởng nào cho ngôn ngữ này -> để null, nên dòng chép sang không bị
                    // hiện nhãn "đã sửa" và cũng không có gì để khôi phục.
                    DefaultText = null,
                    IsSystem = source.IsSystem,
                    UpdatedById = _currentUser.UserId,
                    UpdatedAt = DateTime.Now
                });
            }
            copiedCount = sourceList.Count;
        }

        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();

        _logger.LogInformation("Da them ngon ngu \"{Code}\" ({Name}), chep {Count} cau chu.", code, name, copiedCount);
        return Result.Success(new MessageResponse(copiedCount > 0
            ? $"Đã thêm \"{name}\" và chép sẵn {copiedCount} câu chữ để dịch dần."
            : $"Đã thêm \"{name}\". Bảng dịch của ngôn ngữ này đang trống."));
    }

    public async Task<Result> UpdateLanguage(string code, LanguageUpdateRequest request, CancellationToken cancellationToken)
    {
        code = (code ?? "").Trim().ToLowerInvariant();
        var language = await _db.AppLanguage.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
        if (language is null) return Result.NotFound(new MessageResponse("Không tìm thấy ngôn ngữ này."));

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
            return Result.Invalid(new MessageResponse("Hãy nhập tên hiển thị của ngôn ngữ (tối đa 80 ký tự)."));

        // Tắt hết mọi ngôn ngữ thì giao diện không còn chữ nào để hiện — chặn lại ngay ở đây.
        if (!request.IsActive && !await _db.AppLanguage.AnyAsync(x => x.Code != code && x.IsActive, cancellationToken))
            return Result.Invalid(new MessageResponse("Phải còn ít nhất một ngôn ngữ đang bật."));

        language.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.LocaleTag) && request.LocaleTag.Trim().Length <= 20)
            language.LocaleTag = request.LocaleTag.Trim();
        language.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();
        return Result.Success(new MessageResponse("Đã cập nhật ngôn ngữ."));
    }

    public async Task<Result> DeleteLanguage(string code, CancellationToken cancellationToken)
    {
        code = (code ?? "").Trim().ToLowerInvariant();
        var language = await _db.AppLanguage.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
        if (language is null) return Result.NotFound(new MessageResponse("Không tìm thấy ngôn ngữ này."));

        if (language.IsSystem)
            return Result.Invalid(new MessageResponse(
                $"\"{language.Name}\" là ngôn ngữ đi kèm phần mềm (có sẵn file wwwroot/locales/{code}.json) nên không xóa được. "
                + "Muốn ẩn khỏi nút chuyển ngôn ngữ thì tắt trạng thái hoạt động."));

        var rowList = await _db.Translation.Where(x => x.Lang == code).ToListAsync(cancellationToken);
        _db.Translation.RemoveRange(rowList);
        _db.AppLanguage.Remove(language);
        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();

        _logger.LogInformation("Da xoa ngon ngu \"{Code}\" cung {Count} cau chu.", code, rowList.Count);
        return Result.Success(new MessageResponse($"Đã xóa \"{language.Name}\" cùng {rowList.Count} câu chữ đã dịch."));
    }

    public async Task<Result> GetGroupList(CancellationToken cancellationToken)
    {
        // Gom nhóm trong bộ nhớ chứ không bằng SQL: cắt chuỗi theo dấu chấm đầu tiên không dịch được sang
        // SQL giống nhau trên cả Sqlite/SqlServer/Oracle, mà bảng này chỉ vài nghìn dòng.
        var rowList = await _db.Translation.AsNoTracking()
            .Select(x => new { x.ResourceKey, x.TranslatedText, x.DefaultText })
            .ToListAsync(cancellationToken);

        var groupList = rowList
            .GroupBy(x => GroupOf(x.ResourceKey))
            .Select(g => new TranslationGroupDto(
                g.Key,
                g.Select(x => x.ResourceKey).Distinct().Count(),
                g.Where(x => x.DefaultText != null && x.DefaultText != x.TranslatedText)
                 .Select(x => x.ResourceKey).Distinct().Count()))
            .OrderBy(x => x.Group, StringComparer.Ordinal)
            .ToList();

        return Result.Success(groupList);
    }

    public async Task<Result> GetList(string? keyword, string? group, string? source, int page = 1, int pageSize = 30, CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 30 : pageSize;

        var query = _db.Translation.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // Tìm cả trong KHÓA lẫn trong CÂU CHỮ: người dịch thường chỉ nhớ chữ đang hiện trên màn hình
            // ("Bối cảnh 3D"), không ai nhớ khóa "course-detail.boiCanh3d".
            var loweredKeyword = keyword.Trim().ToLower();
            query = query.Where(x => x.ResourceKey.ToLower().Contains(loweredKeyword)
                                  || x.TranslatedText.ToLower().Contains(loweredKeyword));
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            // Khóa của nhóm G là khóa bắt đầu bằng "G." — so bằng tiền tố chứ không cắt chuỗi, để dịch được
            // sang SQL trên mọi provider.
            var prefix = group.Trim() + ".";
            query = query.Where(x => x.ResourceKey.StartsWith(prefix));
        }

        query = source switch
        {
            "system" => query.Where(x => x.IsSystem),
            "custom" => query.Where(x => !x.IsSystem),
            "customized" => query.Where(x => x.DefaultText != null && x.DefaultText != x.TranslatedText),
            _ => query
        };

        // Bộ lọc bắt theo TỪNG DÒNG (khóa, ngôn ngữ) nhưng kết quả trả về theo KHÓA — tìm "3D Scene" phải ra
        // cả dòng tiếng Việt của khóa đó, nếu không thì sửa xong một bên mà bên kia không thấy để sửa theo.
        var matchedKeyQuery = query.Select(x => x.ResourceKey).Distinct();

        var totalCount = await matchedKeyQuery.CountAsync(cancellationToken);
        var pagedKeyList = await matchedKeyQuery
            .OrderBy(x => x)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var rowList = await _db.Translation.AsNoTracking()
            .Where(x => pagedKeyList.Contains(x.ResourceKey))
            .Select(x => new
            {
                x.ResourceKey, x.Lang, x.TranslatedText, x.DefaultText, x.IsSystem, x.Note, x.UpdatedAt,
                UpdatedByName = x.UpdatedBy != null ? x.UpdatedBy.FullName : null
            })
            .ToListAsync(cancellationToken);

        var itemList = rowList
            .GroupBy(x => x.ResourceKey)
            .Select(g => new TranslationRowDto(
                g.Key,
                GroupOf(g.Key),
                g.Any(x => x.IsSystem),
                g.Any(x => x.DefaultText != null && x.DefaultText != x.TranslatedText),
                g.ToDictionary(x => x.Lang, x => x.TranslatedText),
                g.Where(x => x.DefaultText != null).ToDictionary(x => x.Lang, x => x.DefaultText!),
                // Ghi chú và người sửa lấy theo dòng được động vào gần nhất trong các ngôn ngữ của khóa.
                g.OrderByDescending(x => x.UpdatedAt).Select(x => x.Note).FirstOrDefault(x => x != null),
                g.Max(x => x.UpdatedAt),
                g.OrderByDescending(x => x.UpdatedAt).Select(x => x.UpdatedByName).FirstOrDefault(x => x != null)))
            .OrderBy(x => x.ResourceKey, StringComparer.Ordinal)
            .ToList();

        return Result.Success(new PagedResult<TranslationRowDto>(itemList, totalCount, page, pageSize));
    }

    public async Task<Result> Create(TranslationSaveRequest request, CancellationToken cancellationToken)
    {
        var resourceKey = (request.ResourceKey ?? "").Trim();
        var error = ValidateKey(resourceKey);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        if (await _db.Translation.AnyAsync(x => x.ResourceKey == resourceKey, cancellationToken))
            return Result.Conflict(new MessageResponse($"Khóa \"{resourceKey}\" đã có trong bảng dịch — hãy tìm và sửa dòng đó."));

        var textByLang = request.TextByLang ?? new Dictionary<string, string>();
        if (textByLang.Count == 0 || textByLang.Values.All(string.IsNullOrWhiteSpace))
            return Result.Invalid(new MessageResponse("Hãy nhập câu chữ cho ít nhất một ngôn ngữ."));

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var now = DateTime.Now;

        foreach (var (lang, text) in textByLang)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Length > 2000)
                return Result.Invalid(new MessageResponse("Câu chữ không được dài quá 2000 ký tự."));

            _db.Translation.Add(new Translation
            {
                ResourceKey = resourceKey,
                Lang = lang.Trim().ToLowerInvariant(),
                TranslatedText = text.Trim(),
                // Khóa của công ty không có bản xuất xưởng nào để đối chiếu -> DefaultText để null, nhờ đó
                // nó không bao giờ bị hiện nhãn "đã sửa" và cũng không có gì để khôi phục.
                DefaultText = null,
                IsSystem = false,
                Note = note,
                UpdatedById = _currentUser.UserId,
                UpdatedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();

        _logger.LogInformation("Da them khoa dich \"{Key}\" boi nguoi dung #{UserId}.", resourceKey, _currentUser.UserId);
        return Result.Success(new MessageResponse($"Đã thêm khóa \"{resourceKey}\". Tải lại trang để thấy chữ mới."));
    }

    public async Task<Result> Update(TranslationSaveRequest request, CancellationToken cancellationToken)
    {
        var resourceKey = (request.ResourceKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(resourceKey))
            return Result.Invalid(new MessageResponse("Thiếu khóa dịch."));

        var rowList = await _db.Translation.Where(x => x.ResourceKey == resourceKey).ToListAsync(cancellationToken);
        if (rowList.Count == 0) return Result.NotFound(new MessageResponse("Không tìm thấy khóa dịch này."));

        var textByLang = request.TextByLang ?? new Dictionary<string, string>();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var now = DateTime.Now;
        var isSystemKey = rowList.Any(x => x.IsSystem);

        foreach (var (rawLang, rawText) in textByLang)
        {
            var lang = rawLang.Trim().ToLowerInvariant();
            var text = (rawText ?? "").Trim();
            if (text.Length > 2000)
                return Result.Invalid(new MessageResponse("Câu chữ không được dài quá 2000 ký tự."));

            var row = rowList.FirstOrDefault(x => x.Lang == lang);
            if (row is null)
            {
                // Ngôn ngữ mới được thêm cho một khóa đã có (VD khóa cũ chỉ có tiếng Việt, giờ dịch sang Anh).
                if (text.Length == 0) continue;
                _db.Translation.Add(new Translation
                {
                    ResourceKey = resourceKey,
                    Lang = lang,
                    TranslatedText = text,
                    DefaultText = null,
                    IsSystem = isSystemKey,
                    Note = note,
                    UpdatedById = _currentUser.UserId,
                    UpdatedAt = now
                });
                continue;
            }

            // Xóa trắng câu chữ của một khóa HỆ THỐNG là bỏ luôn chữ trên giao diện đó — chặn lại và nói rõ
            // cách đúng là bấm "Khôi phục mặc định".
            if (text.Length == 0)
            {
                if (row.IsSystem)
                    return Result.Invalid(new MessageResponse(
                        $"Không được để trống câu chữ của khóa hệ thống ({lang}). "
                        + "Muốn trả về bản gốc thì bấm \"Khôi phục mặc định\"."));
                continue;
            }

            if (row.TranslatedText == text && row.Note == note) continue;

            row.TranslatedText = text;
            row.Note = note;
            row.UpdatedById = _currentUser.UserId;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();

        return Result.Success(new MessageResponse("Đã lưu bản dịch. Tải lại trang để thấy chữ mới."));
    }

    public async Task<Result> Reset(TranslationResetRequest request, CancellationToken cancellationToken)
    {
        var resourceKey = (request.ResourceKey ?? "").Trim();
        var rowList = await _db.Translation
            .Where(x => x.ResourceKey == resourceKey && x.DefaultText != null)
            .ToListAsync(cancellationToken);

        if (rowList.Count == 0)
            return Result.Invalid(new MessageResponse("Khóa này do công ty tự thêm nên không có bản gốc để khôi phục."));

        var now = DateTime.Now;
        foreach (var row in rowList)
        {
            if (row.TranslatedText == row.DefaultText) continue;
            row.TranslatedText = row.DefaultText!;
            row.Note = null;
            row.UpdatedById = _currentUser.UserId;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();

        return Result.Success(new MessageResponse("Đã khôi phục bản dịch gốc. Tải lại trang để thấy chữ cũ."));
    }

    public async Task<Result> Delete(string resourceKey, CancellationToken cancellationToken)
    {
        resourceKey = (resourceKey ?? "").Trim();
        var rowList = await _db.Translation.Where(x => x.ResourceKey == resourceKey).ToListAsync(cancellationToken);
        if (rowList.Count == 0) return Result.NotFound(new MessageResponse("Không tìm thấy khóa dịch này."));

        if (rowList.Any(x => x.IsSystem))
            return Result.Invalid(new MessageResponse(
                "Khóa của hệ thống không xóa được vì màn hình đang dùng nó. Muốn trả về chữ gốc thì bấm \"Khôi phục mặc định\"."));

        _db.Translation.RemoveRange(rowList);
        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();

        _logger.LogInformation("Da xoa khoa dich \"{Key}\".", resourceKey);
        return Result.Success(new MessageResponse($"Đã xóa khóa \"{resourceKey}\"."));
    }

    // ==================== DỊCH BẰNG AI ====================

    /// <summary>Số khóa tối đa cho MỘT lượt gọi. Màn hình dịch tự cắt lô theo con số này rồi gọi nhiều lượt.
    ///
    /// Không nhận cả nghìn khóa trong một yêu cầu vì ba lý do, theo thứ tự quan trọng: câu lệnh quá dài thì
    /// model bắt đầu bỏ sót khóa ở giữa; một lượt gọi kéo vài phút sẽ chạm timeout của trình duyệt mà người
    /// dùng không biết nó còn sống hay đã chết; và hỏng ở khóa thứ 900 thì mất trắng cả 900 khóa trước đó.
    /// Cắt lô nhỏ đổi lại được thanh tiến độ, nút dừng, và mỗi lô hỏng chỉ mất đúng lô đó.</summary>
    private const int MaxAiBatchKey = 40;

    /// <summary>Ghi chú tự gắn cho dòng do AI dịch. Bản dịch máy phải PHÂN BIỆT ĐƯỢC với bản người thật đã
    /// soát: không để lại dấu vết thì vài tháng sau không ai biết câu nào đã có người đọc lại, câu nào còn
    /// nguyên si máy dịch. Chỉ gắn khi dòng đó chưa có ghi chú, để không xóa mất lý do người trước đã ghi.</summary>
    private const string AiNote = "Dịch tự động bằng AI — cần soát lại";

    public async Task<Result> AiTranslate(AiTranslateRequest request, CancellationToken cancellationToken)
    {
        if (!_ai.IsConfigured)
            return Result.Invalid(new MessageResponse(
                "Chưa cấu hình máy chủ AI nội bộ (mục Ollama trong appsettings.json). "
                + "Vui lòng liên hệ quản trị viên hệ thống."));

        var sourceLang = (request.SourceLang ?? "").Trim().ToLowerInvariant();
        var targetLang = (request.TargetLang ?? "").Trim().ToLowerInvariant();

        if (sourceLang.Length == 0 || targetLang.Length == 0)
            return Result.Invalid(new MessageResponse("Thiếu ngôn ngữ nguồn hoặc ngôn ngữ đích."));
        if (sourceLang == targetLang)
            return Result.Invalid(new MessageResponse("Ngôn ngữ nguồn và ngôn ngữ đích phải khác nhau."));

        var languageList = await _db.AppLanguage.AsNoTracking()
            .Where(x => x.Code == sourceLang || x.Code == targetLang)
            .ToListAsync(cancellationToken);

        var sourceLanguage = languageList.FirstOrDefault(x => x.Code == sourceLang);
        var targetLanguage = languageList.FirstOrDefault(x => x.Code == targetLang);
        if (sourceLanguage is null || targetLanguage is null)
            return Result.Invalid(new MessageResponse("Ngôn ngữ nguồn hoặc ngôn ngữ đích không có trong hệ thống."));

        var keyList = (request.ResourceKeyList ?? [])
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct()
            .ToList();

        if (keyList.Count == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn khóa dịch nào."));
        if (keyList.Count > MaxAiBatchKey)
            return Result.Invalid(new MessageResponse($"Mỗi lượt chỉ dịch được tối đa {MaxAiBatchKey} khóa."));

        var rowList = await _db.Translation
            .Where(x => keyList.Contains(x.ResourceKey) && (x.Lang == sourceLang || x.Lang == targetLang))
            .ToListAsync(cancellationToken);

        var sourceRowByKey = rowList.Where(x => x.Lang == sourceLang).ToDictionary(x => x.ResourceKey);
        var targetRowByKey = rowList.Where(x => x.Lang == targetLang).ToDictionary(x => x.ResourceKey);

        var pendingList = new List<UiTextToTranslateDto>();
        var skippedCount = 0;

        foreach (var key in keyList)
        {
            // Không có câu nguồn thì chẳng có gì để dịch — bịa ra từ chính cái khóa kỹ thuật còn tệ hơn để
            // trống, vì nhìn vào bảng sẽ tưởng dòng đó đã dịch xong rồi.
            if (!sourceRowByKey.TryGetValue(key, out var sourceRow) || string.IsNullOrWhiteSpace(sourceRow.TranslatedText))
            {
                skippedCount++;
                continue;
            }

            var targetRow = targetRowByKey.GetValueOrDefault(key);
            if (!request.Overwrite && targetRow is not null && !string.IsNullOrWhiteSpace(targetRow.TranslatedText))
            {
                skippedCount++;
                continue;
            }

            pendingList.Add(new UiTextToTranslateDto(key, sourceRow.TranslatedText));
        }

        if (pendingList.Count == 0)
            return Result.Success(new AiTranslateResponse(0, skippedCount, new Dictionary<string, string>(), [],
                "Không có dòng nào cần dịch trong lô này."));

        Dictionary<string, string> resultByKey;
        try
        {
            resultByKey = await _ai.TranslateUiTextAsync(
                sourceLanguage.Name, sourceLanguage.LocaleTag,
                targetLanguage.Name, targetLanguage.LocaleTag,
                pendingList, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dich bang AI that bai ({Source} -> {Target}, {Count} khoa).",
                sourceLang, targetLang, pendingList.Count);
            return Result.ExternalServiceFailed(new MessageResponse(
                "Máy chủ AI đang không phản hồi nên chưa dịch được. Vui lòng thử lại sau ít phút."));
        }

        // Chỉ nhận lại đúng những khóa đã gửi đi: model thỉnh thoảng bịa thêm khóa hoặc trả về khóa bị cắt
        // cụt, ghi thẳng vào bảng thì sinh ra dòng rác mà sau này không ai truy được từ đâu ra.
        var acceptedByKey = new Dictionary<string, string>();
        var failedKeyList = new List<string>();

        foreach (var item in pendingList)
        {
            if (resultByKey.TryGetValue(item.ResourceKey, out var text) && !string.IsNullOrWhiteSpace(text))
                acceptedByKey[item.ResourceKey] = text.Length > 2000 ? text[..2000] : text;
            else
                failedKeyList.Add(item.ResourceKey);
        }

        if (request.Preview)
            return Result.Success(new AiTranslateResponse(acceptedByKey.Count, skippedCount, acceptedByKey, failedKeyList,
                $"Đã dịch thử {acceptedByKey.Count} câu."));

        var now = DateTime.Now;
        foreach (var (key, text) in acceptedByKey)
        {
            var targetRow = targetRowByKey.GetValueOrDefault(key);
            if (targetRow is null)
            {
                // Ngôn ngữ đích chưa có dòng nào cho khóa này (VD ngôn ngữ mới thêm mà không chép sẵn câu chữ).
                _db.Translation.Add(new Translation
                {
                    ResourceKey = key,
                    Lang = targetLang,
                    TranslatedText = text,
                    // Bản dịch máy KHÔNG phải bản xuất xưởng: để DefaultText null thì nó không bị coi là bản
                    // gốc để "khôi phục" về sau, và cũng không làm dòng đó mang nhãn "đã sửa" một cách vô cớ.
                    DefaultText = null,
                    IsSystem = sourceRowByKey[key].IsSystem,
                    Note = AiNote,
                    UpdatedById = _currentUser.UserId,
                    UpdatedAt = now,
                });
                continue;
            }

            if (targetRow.TranslatedText == text) continue;

            targetRow.TranslatedText = text;
            if (string.IsNullOrWhiteSpace(targetRow.Note)) targetRow.Note = AiNote;
            targetRow.UpdatedById = _currentUser.UserId;
            targetRow.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        ClearCache();

        _logger.LogInformation("AI da dich {Count} khoa tu {Source} sang {Target} (bo qua {Skipped}, truot {Failed}).",
            acceptedByKey.Count, sourceLang, targetLang, skippedCount, failedKeyList.Count);

        var message = $"Đã dịch {acceptedByKey.Count} câu sang {targetLanguage.Name}.";
        if (failedKeyList.Count > 0) message += $" {failedKeyList.Count} câu AI không trả về, cần dịch tay.";

        return Result.Success(new AiTranslateResponse(acceptedByKey.Count, skippedCount, acceptedByKey, failedKeyList, message));
    }

    /// <summary>Tiền tố trước dấu chấm đầu tiên. Khóa không có dấu chấm xếp vào nhóm "khác".</summary>
    private static string GroupOf(string resourceKey)
    {
        var dotIndex = resourceKey.IndexOf('.');
        return dotIndex <= 0 ? "khác" : resourceKey[..dotIndex];
    }

    private static string? ValidateKey(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey)) return "Hãy nhập khóa dịch.";
        if (resourceKey.Length > 150) return "Khóa dịch không được dài quá 150 ký tự.";

        // Khóa được nhúng thẳng vào thuộc tính data-i18n trong HTML và tra bằng t() ở JavaScript, nên chỉ
        // nhận bộ ký tự an toàn — dấu nháy hay dấu ngoặc lọt vào là hỏng cả markup lẫn phép tra cứu.
        if (!resourceKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            return "Khóa dịch chỉ được dùng chữ không dấu, số và các dấu . - _ (VD \"congtruong.gianGiao\").";

        return null;
    }

    /// <summary>Xóa bộ đệm từ điển của mọi ngôn ngữ sau khi có thay đổi.</summary>
    private void ClearCache()
    {
        // Quét theo dữ liệu thật (cả bảng ngôn ngữ lẫn bảng dịch) chứ không theo một danh sách cố định:
        // bỏ sót một ngôn ngữ là sửa xong vẫn thấy chữ cũ ở đúng ngôn ngữ đó.
        var langList = _db.AppLanguage.Select(x => x.Code).ToList()
            .Union(_db.Translation.Select(x => x.Lang).Distinct().ToList());
        foreach (var lang in langList) _cache.Remove(CachePrefix + lang);
    }
}
