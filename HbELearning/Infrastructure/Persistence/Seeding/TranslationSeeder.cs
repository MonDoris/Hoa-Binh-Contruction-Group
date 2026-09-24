// File: Infrastructure/Persistence/Seeding/TranslationSeeder.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Infrastructure.Persistence.Seeding;

/// <summary>Nạp các file bản dịch xuất xưởng (wwwroot/locales/*.json) vào bảng HBE_TRANSLATION.
///
/// Chạy MỖI LẦN KHỞI ĐỘNG chứ không chỉ lần đầu, vì bản cập nhật phần mềm thường kèm thêm khóa mới hoặc
/// sửa lại câu chữ cũ. Quy tắc gộp được viết để KHÔNG BAO GIỜ đè lên chữ công ty đã sửa:
///
///   • Khóa chưa có trong CSDL          -> thêm mới, đánh dấu là của hệ thống.
///   • Khóa đã có, công ty CHƯA sửa     -> nhận luôn câu chữ mới của bản cập nhật.
///   • Khóa đã có, công ty ĐÃ sửa       -> giữ nguyên chữ của công ty, chỉ cập nhật lại bản gốc đối chiếu
///                                         (DefaultText) để nút "Khôi phục mặc định" trả về đúng bản mới nhất.
///   • Khóa do công ty tự thêm          -> không đụng tới, file JSON không có khóa đó.
///
/// Khóa bị GỠ khỏi file JSON cũng không xóa khỏi CSDL: có thể chỉ là đổi tên file/nhánh triển khai, mà xóa
/// đi là mất luôn phần công ty đã dịch cho khóa đó.</summary>
public static class TranslationSeeder
{
    public static async Task SeedAsync(AppDbContext db, string webRootPath, ILogger logger)
    {
        var folder = Path.Combine(webRootPath, "locales");
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("Khong tim thay thu muc {Folder} — bo qua buoc nap ban dich.", folder);
            return;
        }

        // Nạp một lần rồi tra trong bộ nhớ: gọi CSDL cho từng khóa là gần 2000 lượt truy vấn mỗi lần khởi động.
        var rowByKey = await db.Translation
            .ToDictionaryAsync(x => KeyOf(x.ResourceKey, x.Lang));

        // Mỗi file locale phải có một bản ghi ngôn ngữ tương ứng, nếu không thì nút chuyển ngôn ngữ và màn
        // hình dịch đều không biết ngôn ngữ đó tồn tại. Đánh dấu IsSystem để không ai xóa nhầm — file vẫn
        // nằm đó và sẽ nạp lại ở lần khởi động sau.
        var existingLangList = await db.AppLanguage.ToDictionaryAsync(x => x.Code);

        var newRowList = new List<Translation>();
        var updatedCount = 0;

        foreach (var filePath in Directory.GetFiles(folder, "*.json").OrderBy(x => x))
        {
            var lang = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

            if (!existingLangList.TryGetValue(lang, out var languageRow))
            {
                languageRow = new AppLanguage
                {
                    Code = lang,
                    Name = DefaultLanguageName.GetValueOrDefault(lang, lang.ToUpperInvariant()),
                    LocaleTag = DefaultLocaleTag.GetValueOrDefault(lang, lang),
                    OrderIndex = lang == "vi" ? 0 : 1,
                    IsSystem = true
                };
                db.AppLanguage.Add(languageRow);
                existingLangList[lang] = languageRow;
            }
            else if (!languageRow.IsSystem)
            {
                // Ngôn ngữ công ty tự thêm nay đã có file dịch đi kèm phần mềm -> trở thành ngôn ngữ hệ thống.
                languageRow.IsSystem = true;
            }

            Dictionary<string, string>? dictionary;
            try
            {
                dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(filePath));
            }
            catch (JsonException ex)
            {
                // Một file JSON hỏng (thừa dấu phẩy, thiếu ngoặc kép) không được làm chết cả lần khởi động —
                // các ngôn ngữ còn lại vẫn phải nạp được.
                logger.LogWarning("File ban dich {File} sai cu phap JSON nen bi bo qua: {Message}", filePath, ex.Message);
                continue;
            }

            if (dictionary is null) continue;

            foreach (var (resourceKey, text) in dictionary)
            {
                // Khóa/câu chữ vượt giới hạn cột thì bỏ qua thay vì để SaveChanges ném lỗi và chặn khởi động.
                if (string.IsNullOrWhiteSpace(resourceKey) || resourceKey.Length > 150 || text.Length > 2000)
                {
                    logger.LogWarning("Bo qua khoa dich qua dai trong {File}: {Key}", filePath, resourceKey);
                    continue;
                }

                if (!rowByKey.TryGetValue(KeyOf(resourceKey, lang), out var row))
                {
                    newRowList.Add(new Translation
                    {
                        ResourceKey = resourceKey,
                        Lang = lang,
                        TranslatedText = text,
                        DefaultText = text,
                        IsSystem = true
                    });
                    continue;
                }

                // Khóa công ty tự thêm mà trùng tên với khóa hệ thống mới ra: để nguyên của công ty.
                if (!row.IsSystem || row.DefaultText == text) continue;

                // Chưa ai sửa (chữ đang dùng vẫn đúng bằng bản gốc cũ) -> nhận câu chữ mới của bản cập nhật.
                if (row.TranslatedText == row.DefaultText) row.TranslatedText = text;
                row.DefaultText = text;
                updatedCount++;
            }
        }

        if (newRowList.Count > 0) db.Translation.AddRange(newRowList);

        // Luôn gọi SaveChanges: kể cả khi không có câu chữ nào đổi thì vẫn có thể có bản ghi ngôn ngữ mới
        // vừa được thêm ở vòng lặp trên.
        await db.SaveChangesAsync();
        if (newRowList.Count == 0 && updatedCount == 0) return;
        logger.LogInformation("Ban dich he thong: them {Added} dong, cap nhat ban goc {Updated} dong.",
            newRowList.Count, updatedCount);
    }

    private static string KeyOf(string resourceKey, string lang) => resourceKey + "\u0001" + lang;

    /// <summary>Tên và thẻ locale mặc định cho các ngôn ngữ đi kèm phần mềm. Ngôn ngữ lạ (file locale do bên
    /// triển khai bỏ thêm vào) lấy tạm chính mã làm tên — Admin sửa lại ở màn hình "Dịch ngôn ngữ".</summary>
    private static readonly Dictionary<string, string> DefaultLanguageName = new()
    {
        ["vi"] = "Tiếng Việt",
        ["en"] = "English",
    };

    private static readonly Dictionary<string, string> DefaultLocaleTag = new()
    {
        ["vi"] = "vi-VN",
        ["en"] = "en-US",
    };
}
