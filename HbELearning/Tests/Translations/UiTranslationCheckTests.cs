using HbELearning.Application.Features.Translations;

namespace HbELearning.Tests.Translations;

/// <summary>
/// Bộ soát câu AI dịch trước khi ghi vào bảng: phải chặn đúng các lỗi cấu trúc (mất chỗ giữ chỗ, mất số, trả lời
/// bằng tiếng Việt, sai hệ chữ), nhưng không được chặn nhầm bản dịch đúng — chặn nhầm thì câu đó bị để trống.
/// </summary>
public class UiTranslationCheckTests
{
    [Theory]
    [InlineData("Chứng nhận hoàn thành", "ใบรับรองการสำเร็จ", "th")]
    [InlineData("Còn {count} ngày", "เหลืออีก {count} วัน", "th")]
    [InlineData("Đã hoàn thành <b>{name}</b>", "<b>{name}</b> を完了しました", "ja")]
    [InlineData("Tối đa 40 câu", "最大 40 問", "ja")]
    [InlineData("Lưu", "Save", "en")]
    [InlineData("HB Minerva", "HB Minerva", "th")]      // tên riêng giữ nguyên là đúng
    [InlineData("Email", "Email", "ja")]
    public void Accepts_good_translation(string source, string target, string lang)
        => Assert.Null(UiTranslationCheck.FindProblem(source, target, lang));

    [Theory]
    [InlineData("Còn {count} ngày", "เหลืออีก วัน", "th")]                 // mất chỗ giữ chỗ
    [InlineData("Còn {count} ngày", "เหลืออีก {จำนวน} วัน", "th")]          // dịch luôn tên chỗ giữ chỗ
    [InlineData("Đã hoàn thành <b>{name}</b>", "{name} を完了しました", "ja")] // mất thẻ HTML
    [InlineData("Tối đa 40 câu", "最大の問題", "ja")]                        // mất con số
    [InlineData("Chứng nhận hoàn thành", "Chứng nhận hoàn thành", "th")]    // chép nguyên nguồn
    [InlineData("Chứng nhận hoàn thành", "ใบรับรอง hoàn thành", "th")]      // còn chữ tiếng Việt
    [InlineData("Chứng nhận hoàn thành", "Certificate of completion", "th")] // sai hệ chữ
    [InlineData("Lưu", "", "th")]
    public void Rejects_broken_translation(string source, string target, string lang)
        => Assert.NotNull(UiTranslationCheck.FindProblem(source, target, lang));

    [Fact]
    public void Rejects_runaway_output()
        => Assert.NotNull(UiTranslationCheck.FindProblem("Lưu", new string('ก', 200), "th"));
}
