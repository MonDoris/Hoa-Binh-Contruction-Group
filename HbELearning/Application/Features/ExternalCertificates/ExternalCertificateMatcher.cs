// File: Application/Features/ExternalCertificates/ExternalCertificateMatcher.cs
using System.Globalization;
using System.Text;

namespace HbELearning.Application.Features.ExternalCertificates;

/// <summary>Thông tin nhân viên khai khi gửi giấy (đã qua kiểm hợp lệ).</summary>
public sealed record CertificateClaim(string Title, string Issuer, string? CertificateNo, DateTime IssuedAt, DateTime? ExpiredAt);

/// <summary>Kết quả so MỘT mục giữa thông tin khai và thứ AI đọc được trên giấy.</summary>
/// <param name="Field">"document" | "name" | "title" | "issuer" | "no" | "issued" | "expired".</param>
/// <param name="Ok">true = khớp, false = lệch, null = không so được (giấy không ghi / AI không đọc ra) — không
/// tính là lệch.</param>
/// <param name="Found">Giá trị đọc được trên giấy, để hiện cạnh giá trị khai.</param>
public sealed record CertificateCheck(string Field, bool? Ok, string? Found, string? Message);

/// <summary>Kết luận đối chiếu.</summary>
/// <param name="CertificateNoFromPaper">Nhân viên bỏ trống số hiệu mà giấy có ghi → lấy theo giấy.</param>
/// <param name="ExpiredAtFromPaper">Nhân viên bỏ trống ngày hết hạn mà giấy có ghi → lấy theo giấy.</param>
public sealed record CertificateVerdict(
    bool IsApproved, string? RejectReason, IReadOnlyList<CertificateCheck> CheckList,
    string? CertificateNoFromPaper, DateTime? ExpiredAtFromPaper);

/// <summary>
/// So thứ AI đọc được trên giấy với thông tin nhân viên khai và họ tên tài khoản — phần QUYẾT ĐỊNH của việc tự
/// xác nhận chứng nhận bên ngoài. Viết thành luật cố định thay vì hỏi luôn AI "có khớp không": cùng một tờ giấy
/// thì lần nào cũng ra cùng một kết luận, và mỗi lần từ chối đều chỉ ra được đúng mục nào lệch.
///
/// Luật nới cho lỗi đọc chữ (OCR), không nới cho khác biệt thật:
///   • so không dấu, không phân biệt hoa thường, bỏ dấu câu — model hay đánh rơi dấu tiếng Việt;
///   • họ tên đổi thứ tự được (giấy nước ngoài ghi tên trước họ), thiếu tên đệm được, lệch một hai ký tự được;
///   • tên chứng nhận / đơn vị cấp: khai gọn hơn giấy, viết tắt ("ATLĐ") vẫn khớp;
///   • ngày tháng đảo ngày ↔ tháng vẫn khớp (giấy Việt Nam ghi ngày/tháng, AI đôi khi đọc ngược).
/// Mục giấy không ghi / AI không đọc ra thì bỏ qua, TRỪ họ tên và tên chứng nhận — hai thứ làm nên một tờ chứng nhận.
/// </summary>
public static class ExternalCertificateMatcher
{
    private const double TitleThreshold = 0.6;
    private const double IssuerThreshold = 0.5;
    private const double NameThreshold = 0.85;

    /// <summary>Danh xưng đứng đầu họ tên trên giấy ("Ông Nguyễn Văn A", "Mr. John Smith"). KHÔNG có "anh",
    /// "chị": đó cũng là tên gọi (Nguyễn Minh Anh).</summary>
    private static readonly HashSet<string> HonorificSet = ["ong", "ba", "mr", "mrs", "ms", "miss", "dr", "sir", "madam"];

    private const string RetakeHint = "Hãy chụp lại rõ hơn: đủ sáng, không lóa, thấy trọn tờ giấy.";

    public static CertificateVerdict Match(CertificateReading reading, string accountName, CertificateClaim claim)
    {
        var checkList = new List<CertificateCheck>();
        string? noFromPaper = null;
        DateTime? expiredFromPaper = null;

        if (!reading.IsCertificate)
        {
            var detail = string.Join(" ", new[] { Quote(reading.DocumentType), reading.Note }.Where(x => !string.IsNullOrWhiteSpace(x)));
            checkList.Add(new("document", false, reading.DocumentType,
                $"AI không nhận ra đây là giấy chứng nhận / chứng chỉ{(detail.Length > 0 ? $" ({detail.TrimEnd('.')})" : "")}."));
            return Verdict(checkList, noFromPaper, expiredFromPaper, needRetakeHint: !reading.IsReadable);
        }
        checkList.Add(new("document", true, reading.DocumentType, null));

        // ---- Họ tên người được cấp: phải là chủ tài khoản ----
        if (IsBlank(reading.HolderName))
            checkList.Add(new("name", false, null, "Không đọc được họ tên người được cấp trên giấy."));
        else if (!NameMatches(reading.HolderName!, accountName))
            checkList.Add(new("name", false, reading.HolderName,
                $"Họ tên trên giấy là \"{reading.HolderName}\", không trùng tên tài khoản \"{accountName}\"."));
        else
            checkList.Add(new("name", true, reading.HolderName, null));

        // ---- Tên chứng nhận ----
        if (IsBlank(reading.Title))
            checkList.Add(new("title", false, null, "Không đọc được tên chứng nhận trên giấy."));
        else if (!TextMatches(claim.Title, reading.Title!, TitleThreshold))
            checkList.Add(new("title", false, reading.Title,
                $"Tên chứng nhận khai là \"{claim.Title}\" nhưng trên giấy ghi \"{reading.Title}\"."));
        else
            checkList.Add(new("title", true, reading.Title, null));

        // ---- Đơn vị cấp ----
        if (IsBlank(reading.Issuer))
            checkList.Add(new("issuer", null, null, null));
        else if (!TextMatches(claim.Issuer, reading.Issuer!, IssuerThreshold))
            checkList.Add(new("issuer", false, reading.Issuer,
                $"Đơn vị cấp khai là \"{claim.Issuer}\" nhưng trên giấy ghi \"{reading.Issuer}\"."));
        else
            checkList.Add(new("issuer", true, reading.Issuer, null));

        // ---- Số hiệu ----
        if (!IsBlank(reading.CertificateNo))
        {
            if (IsBlank(claim.CertificateNo))
            {
                noFromPaper = Truncate(reading.CertificateNo!.Trim(), 100);
                checkList.Add(new("no", true, reading.CertificateNo, null));
            }
            else if (NumberMatches(claim.CertificateNo!, reading.CertificateNo!))
                checkList.Add(new("no", true, reading.CertificateNo, null));
            else
                checkList.Add(new("no", false, reading.CertificateNo,
                    $"Số hiệu khai là \"{claim.CertificateNo}\" nhưng trên giấy ghi \"{reading.CertificateNo}\"."));
        }
        else if (!IsBlank(claim.CertificateNo))
            checkList.Add(new("no", null, null, null));

        // ---- Ngày cấp ----
        if (reading.IssuedAt is not { } paperIssued)
            checkList.Add(new("issued", null, null, null));
        else if (SameDate(claim.IssuedAt, paperIssued))
            checkList.Add(new("issued", true, DateText(paperIssued), null));
        else
            checkList.Add(new("issued", false, DateText(paperIssued),
                $"Ngày cấp khai là {DateText(claim.IssuedAt)} nhưng trên giấy ghi {DateText(paperIssued)}."));

        // ---- Ngày hết hạn ----
        if (reading.ExpiredAt is { } paperExpired && paperExpired.Date >= claim.IssuedAt.Date)
        {
            if (claim.ExpiredAt is not { } claimExpired)
            {
                expiredFromPaper = paperExpired.Date;
                checkList.Add(new("expired", true, DateText(paperExpired), null));
            }
            else if (SameDate(claimExpired, paperExpired))
                checkList.Add(new("expired", true, DateText(paperExpired), null));
            else
                checkList.Add(new("expired", false, DateText(paperExpired),
                    $"Ngày hết hạn khai là {DateText(claimExpired)} nhưng trên giấy ghi {DateText(paperExpired)}."));
        }
        else if (claim.ExpiredAt is not null)
            checkList.Add(new("expired", null, null, null));

        var unreadable = checkList.Any(x => x.Ok == false && x.Found is null);
        return Verdict(checkList, noFromPaper, expiredFromPaper, needRetakeHint: unreadable);
    }

    /// <summary>Chỉ so họ tên — dùng lúc AI vừa quét xong, chưa có thông tin khai, để báo sớm cho nhân viên.</summary>
    public static bool NameMatches(string onPaper, string accountName)
    {
        var paper = NameTokens(onPaper);
        var account = NameTokens(accountName);
        if (paper.Count == 0 || account.Count == 0) return false;

        var sortedPaper = string.Join(' ', paper.Order(StringComparer.Ordinal));
        var sortedAccount = string.Join(' ', account.Order(StringComparer.Ordinal));
        if (sortedPaper == sortedAccount || Similarity(sortedPaper, sortedAccount) >= NameThreshold) return true;

        // Thiếu tên đệm (giấy nước ngoài hay bỏ): mọi chữ của bên ngắn đều có ở bên dài, ít nhất hai chữ, và
        // giấy có tên gọi (chữ cuối của tên tài khoản) — "Nguyen Anh" khớp "Nguyễn Thị Minh Anh".
        var (shorter, longer) = paper.Count <= account.Count ? (paper, account) : (account, paper);
        return shorter.Count >= 2 && shorter.All(longer.Contains) && paper.Contains(account[^1]);
    }

    // ==================== SO CHỮ ====================

    private static CertificateVerdict Verdict(List<CertificateCheck> checkList, string? noFromPaper, DateTime? expiredFromPaper, bool needRetakeHint)
    {
        var problemList = checkList.Where(x => x.Ok == false && x.Message is not null).Select(x => x.Message!).ToList();
        if (problemList.Count == 0)
            return new CertificateVerdict(true, null, checkList, noFromPaper, expiredFromPaper);

        if (needRetakeHint) problemList.Add(RetakeHint);
        return new CertificateVerdict(false, Truncate(string.Join(" ", problemList), 1000), checkList, noFromPaper, expiredFromPaper);
    }

    private static bool TextMatches(string claimed, string onPaper, double threshold)
    {
        var a = Fold(claimed);
        var b = Fold(onPaper);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;

        // Khai gọn hơn giấy (hoặc ngược lại): "An toàn lao động" ↔ "Giấy chứng nhận huấn luyện an toàn lao động".
        var (shortText, longText) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shortText.Length >= 6 && $" {longText} ".Contains($" {shortText} ", StringComparison.Ordinal)) return true;

        // Viết tắt: "ATLĐ" ↔ "An Toàn Lao Động", "Trung tâm ATLĐ" ↔ "Trung tâm huấn luyện an toàn lao động".
        var aTokens = a.Split(' ').ToList();
        var bTokens = b.Split(' ').ToList();
        var aExpanded = ExpandAcronym(aTokens, bTokens);
        var bExpanded = ExpandAcronym(bTokens, aTokens);

        var score = Math.Max(
            Math.Max(Dice(aExpanded, bTokens), Dice(aTokens, bExpanded)),
            Similarity(string.Join(' ', aExpanded), string.Join(' ', bExpanded)));
        return score >= threshold;
    }

    /// <summary>Thay mỗi chữ viết tắt bên này bằng cụm chữ bên kia có các chữ cái đầu ghép lại đúng bằng nó.</summary>
    private static List<string> ExpandAcronym(List<string> tokenList, List<string> otherTokenList)
    {
        var result = new List<string>();
        foreach (var token in tokenList)
        {
            if (token.Length is >= 2 and <= 8 && token.All(char.IsLetter) && !otherTokenList.Contains(token))
            {
                var expanded = false;
                for (var i = 0; i + token.Length <= otherTokenList.Count && !expanded; i++)
                {
                    var run = otherTokenList.GetRange(i, token.Length);
                    if (string.Concat(run.Select(x => x[0])) != token) continue;
                    result.AddRange(run);
                    expanded = true;
                }
                if (expanded) continue;
            }
            result.Add(token);
        }
        return result;
    }

    private static bool NumberMatches(string claimed, string onPaper)
    {
        var a = NumberKey(claimed);
        var b = NumberKey(onPaper);
        if (a.Length == 0 || b.Length == 0) return false;
        // Lệch một ký tự cho số hiệu dài: OCR hay nhầm 8 ↔ B, 5 ↔ S.
        return a == b || (Math.Min(a.Length, b.Length) >= 5 && Levenshtein(a, b) <= 1);
    }

    /// <summary>Số hiệu chỉ giữ chữ và số, viết hoa, gộp các cặp ký tự OCR hay nhầm (O ↔ 0, I/L ↔ 1).</summary>
    private static string NumberKey(string text) => new(Fold(text).ToUpperInvariant()
        .Where(char.IsLetterOrDigit)
        .Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c })
        .ToArray());

    private static bool SameDate(DateTime claimed, DateTime onPaper)
        => claimed.Date == onPaper.Date
           || (claimed.Year == onPaper.Year && claimed.Day == onPaper.Month && claimed.Month == onPaper.Day);

    private static List<string> NameTokens(string name)
    {
        var tokenList = Fold(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokenList.Count > 2 && HonorificSet.Contains(tokenList[0])) tokenList.RemoveAt(0);
        return tokenList;
    }

    /// <summary>Chữ thường, không dấu (đ → d), dấu câu thành khoảng trắng, gộp khoảng trắng.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (!char.IsLetterOrDigit(c)) { pendingSpace = true; continue; }
            if (pendingSpace && builder.Length > 0) builder.Append(' ');
            builder.Append(c);
            pendingSpace = false;
        }
        return builder.ToString();
    }

    /// <summary>Hệ số Dice trên tập chữ: 2·|chung| / (|A| + |B|).</summary>
    private static double Dice(List<string> a, List<string> b)
    {
        var setA = a.ToHashSet();
        var setB = b.ToHashSet();
        if (setA.Count + setB.Count == 0) return 0;
        return 2.0 * setA.Count(setB.Contains) / (setA.Count + setB.Count);
    }

    private static double Similarity(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1 : 1 - (double)Levenshtein(a, b) / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
    private static string? Quote(string? value) => IsBlank(value) ? null : $"\"{value!.Trim()}\"";
    private static string DateText(DateTime value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
