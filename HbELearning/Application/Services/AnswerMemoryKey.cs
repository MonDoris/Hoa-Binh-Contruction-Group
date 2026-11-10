// File: Application/Services/AnswerMemoryKey.cs
using System.Text;

namespace HbELearning.Application.Services;

/// <summary>"Khóa nghĩa" của một câu hỏi — hai câu cùng khóa thì HB Minerva coi là cùng một câu hỏi và trả lại câu
/// trả lời đã ghi nhớ (xem <see cref="AiAnswerMemory"/>).
///
/// VÌ SAO KHÔNG SO BẰNG VECTOR: đã đo nomic-embed-text trên câu hỏi tiếng Việt, câu KHÁC nghĩa lại giống nhau hơn
/// câu cùng nghĩa — "nhân viên chính thức / thử việc được nghỉ phép bao nhiêu ngày" giống nhau 0,96, "mang laptop
/// ra khỏi / vào văn phòng" 0,96, trong khi "mấy giờ vào làm" với "công ty bắt đầu làm việc lúc mấy giờ" chỉ 0,79.
/// Đặt ngưỡng nào cũng hoặc trả lời nhầm câu khác nghĩa, hoặc không bắt được gì. Mà trả lời nhầm ở đây là đưa
/// một quy định sai cho cả công ty — bỏ lỡ một lần ghi nhớ chỉ tốn thêm thời gian chờ.
///
/// Nên khóa được dựng từ chính các chữ: giữ mọi âm tiết mang nghĩa (kể cả "vào"/"ra", "không", con số — đổi một
/// chữ ấy là đổi câu hỏi), chỉ bỏ hư từ không đổi nghĩa ("ạ", "vậy", "cho tôi hỏi"...), rồi bỏ qua hoa thường, dấu
/// câu, chữ lặp và thứ tự. Nhờ đó "Tứ diện cháy gồm những yếu tố nào?" và "tứ diện cháy gồm yếu tố gì" là một,
/// còn "mấy giờ vào làm" và "mấy giờ tan làm" là hai.
///
/// Từ để hỏi thì KHÔNG bỏ mà gom về LOẠI câu hỏi: "như thế nào" = "ra sao" = "làm sao", "gì" = "nào", "mấy" =
/// "bao nhiêu". Bỏ hẳn thì "Ai phê duyệt đơn nghỉ phép?" và "Phê duyệt đơn nghỉ phép như thế nào?" cùng một khóa
/// — cùng chủ đề nhưng hỏi hai chuyện khác nhau.
///
/// GIỮ dấu tiếng Việt (khác <see cref="QuickReplyMatcher"/>): "bán"/"bàn", "ca"/"cà" là chữ khác nhau. Chỉ
/// gộp hai kiểu bỏ dấu cũ/mới ("hoà"/"hòa", "thuý"/"thúy") vì bàn phím mỗi người gõ một kiểu.</summary>
public static class AnswerMemoryKey
{
    /// <summary>Khóa nghĩa của câu hỏi, hoặc null nếu không ghi nhớ an toàn được: câu quá ít chữ mang nghĩa ("là
    /// gì vậy?"), hoặc câu trỏ vào ngữ cảnh ("quy định đó áp dụng cho ai?" — "đó" là gì thì tùy hội thoại).</summary>
    public static string? Build(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return null;

        var wordList = SplitWordList(question);
        if (wordList.Any(ReferenceWordSet.Contains)) return null;
        ReplacePhraseList(wordList);

        var keySet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var word in wordList)
        {
            if (FillerWordSet.Contains(word)) continue;
            keySet.Add(QuestionWordMap.TryGetValue(word, out var kind) ? kind : ToToneKey(word));
        }

        if (keySet.Count < MinWordCount) return null;

        var key = string.Join(' ', keySet);
        return key.Length <= MaxKeyLength ? key : null;
    }

    /// <summary>Ít nhất ngần này âm tiết mang nghĩa mới ghi nhớ.</summary>
    private const int MinWordCount = 2;

    /// <summary>Khớp MaxLength của cột AiAnswerMemory.QuestionKey.</summary>
    private const int MaxKeyLength = 400;

    /// <summary>Âm tiết viết thường, đã gộp về dạng dựng sẵn (NFC). Trình gõ khác nhau gửi lên cùng một chữ ở dạng
    /// dựng sẵn hoặc tổ hợp — không gộp thì hai câu giống hệt nhau trên màn hình lại thành hai khóa.</summary>
    private static List<string> SplitWordList(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var wordList = new List<string>();
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
                continue;
            }
            if (builder.Length > 0) { wordList.Add(builder.ToString()); builder.Clear(); }
        }
        if (builder.Length > 0) wordList.Add(builder.ToString());

        return wordList;
    }

    /// <summary>Thay các cụm nhiều âm tiết: cụm hỏi thành loại câu hỏi, lời rào đón thì bỏ. Phải xét theo CỤM chứ
    /// không theo từng chữ: "làm sao" là "thế nào", nhưng "làm" một mình là động từ mang nghĩa ("vào làm"); "xin
    /// hỏi" là lời rào đón, nhưng "xin" trong "xin nghỉ phép" là chính câu hỏi.</summary>
    private static void ReplacePhraseList(List<string> wordList)
    {
        foreach (var (phrase, replacement) in PhraseList)
        {
            for (int i = 0; i + phrase.Length <= wordList.Count; i++)
            {
                var isMatched = true;
                for (int j = 0; j < phrase.Length && isMatched; j++)
                    isMatched = wordList[i + j] == phrase[j];
                if (!isMatched) continue;

                wordList.RemoveRange(i, phrase.Length);
                if (replacement is null) i--;
                else wordList.Insert(i, replacement);
            }
        }
    }

    /// <summary>Loại câu hỏi. Bắt đầu bằng "?" để không bao giờ trùng một âm tiết thật.</summary>
    private const string HowKind = "?how";
    private const string WhatKind = "?what";
    private const string QuantityKind = "?qty";

    /// <summary>Cụm nhiều âm tiết -> loại câu hỏi (null = bỏ đi). Cụm dài xét trước để "làm thế nào" không bị
    /// "thế nào" cắt mất một nửa rồi còn sót chữ "làm".</summary>
    private static readonly (string[] Phrase, string? Replacement)[] PhraseList =
    [
        .. new (string Phrase, string? Replacement)[]
        {
            ("là như thế nào", HowKind), ("làm như thế nào", HowKind), ("làm thế nào", HowKind),
            ("như thế nào", HowKind), ("thế nào", HowKind), ("ra sao", HowKind), ("làm sao", HowKind),
            ("bao nhiêu", QuantityKind),
            ("cho tôi hỏi", null), ("cho mình hỏi", null), ("cho em hỏi", null),
            ("cho tôi biết", null), ("cho mình biết", null), ("cho em biết", null),
            ("xin hỏi", null), ("xin cho biết", null), ("vui lòng", null),
            ("giúp tôi", null), ("giúp mình", null), ("giúp em", null)
        }
        .Select(x => (x.Phrase.Normalize(NormalizationForm.FormC).Split(' '), x.Replacement))
        .OrderByDescending(x => x.Item1.Length)
    ];

    /// <summary>Từ để hỏi một âm tiết -> loại câu hỏi. "gì" và "nào" chung một loại vì "yếu tố gì" và "yếu tố nào"
    /// là một câu. Cố ý KHÔNG có: "ai" (hỏi người), "đâu" (hỏi nơi chốn), "sao" một mình (hỏi lý do: "sao phải nộp
    /// đơn?" khác "nộp đơn ra sao?") — giữ nguyên làm âm tiết mang nghĩa.</summary>
    private static readonly Dictionary<string, string> QuestionWordMap = new(
        new Dictionary<string, string>
        {
            ["gì"] = WhatKind, ["nào"] = WhatKind, ["mấy"] = QuantityKind
        }.ToDictionary(x => x.Key.Normalize(NormalizationForm.FormC), x => x.Value),
        StringComparer.Ordinal);

    /// <summary>Từ chỉ trỏ: câu có chúng thì nghĩa nằm ở câu trước, không ghi nhớ.</summary>
    private static readonly HashSet<string> ReferenceWordSet = new(
        new[] { "đó", "này", "kia", "ấy", "nãy" }.Select(x => x.Normalize(NormalizationForm.FormC)),
        StringComparer.Ordinal);

    /// <summary>Hư từ một âm tiết: trợ từ cuối câu, đại từ người hỏi, từ chỉ số nhiều, "là"/"thì".
    ///
    /// Cố ý KHÔNG có trong danh sách: "có", "không", "được" (câu hỏi được/không được phép), "ra"/"vào"/"ở"/"khi"/
    /// "tại"/"vì" (bỏ thì "khi nào" và "lúc nào", "tại sao" và "vì sao" lẫn với câu khác), "xin", "làm", "thế" (còn
    /// trong "thay thế", "thế chấp").</summary>
    private static readonly HashSet<string> FillerWordSet = new(
        new[]
        {
            "ạ", "à", "ơi", "nhé", "nhỉ", "nha", "hả", "hở", "vậy", "thì", "là",
            "tôi", "mình", "em", "bạn", "tớ",
            "của", "các", "những", "hãy"
        }.Select(x => x.Normalize(NormalizationForm.FormC)),
        StringComparer.Ordinal);

    /// <summary>Đưa dấu thanh (huyền, sắc, hỏi, ngã, nặng) ra cuối âm tiết thành một chữ số, giữ nguyên các dấu
    /// khác (â, ă, ê, ô, ơ, ư, đ). "hoà" và "hòa" cùng thành "hoa2": hai kiểu bỏ dấu chỉ khác nhau ở CHỖ đặt dấu
    /// thanh, còn dấu thanh là gì thì như nhau.</summary>
    private static string ToToneKey(string word)
    {
        var decomposed = word.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        char? tone = null;

        foreach (var c in decomposed)
        {
            if (ToneDigitByMark.TryGetValue(c, out var digit)) tone = digit;
            else builder.Append(c);
        }

        var baseWord = builder.ToString().Normalize(NormalizationForm.FormC);
        return tone is null ? baseWord : baseWord + tone;
    }

    private static readonly Dictionary<char, char> ToneDigitByMark = new()
    {
        ['́'] = '1', // sắc
        ['̀'] = '2', // huyền
        ['̉'] = '3', // hỏi
        ['̃'] = '4', // ngã
        ['̣'] = '5'  // nặng
    };
}
