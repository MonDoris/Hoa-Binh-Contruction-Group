// File: Infrastructure/Import/WordQuestionParser.cs
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HbELearning.Infrastructure.Documents;

namespace HbELearning.Infrastructure.Import;

/// <summary>
/// Bóc tách file Word ngân hàng câu hỏi soạn tự do. Xem <see cref="IWordQuestionParser"/> để biết cách viết
/// tài liệu mà bộ này hiểu.
///
/// Đọc theo HAI LƯỢT chứ không ghi thẳng ra kết quả ngay khi quét:
///   1. Quét tuần tự, xếp mỗi đoạn văn vào một vai (dòng chia bài, câu hỏi, đáp án, đáp án đúng, giải thích,
///      hoặc một mục trong BẢNG ĐÁP ÁN cuối đề) và giữ tất cả ở dạng nháp.
///   2. Ghép bảng đáp án vào đúng câu theo số câu, rồi mới kiểm tra và loại những câu thiếu dữ liệu.
///
/// Phải hai lượt vì đề thi thật rất hay để đáp án ở cuối tài liệu — lúc quét tới câu hỏi thì chưa biết đáp
/// án nào đúng, kiểm tra ngay tại đó sẽ loại sạch cả đề.
/// </summary>
public class WordQuestionParser : IWordQuestionParser
{
    /// <summary>Dòng chia bài: "BÀI 1", "Chương II:", "Phần 3 -", "Chuyên đề 4".</summary>
    private static readonly Regex GroupPattern = new(
        @"^\s*(bài|chương|phần|chuyên\s*đề)\s*(\d{1,3}|[ivxlc]{1,7})\s*([.:)\-–—]|\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Dòng câu hỏi: "Câu 1:", "Câu hỏi 2 -", "1." hoặc "12)". Nhóm "number" là số câu.</summary>
    private static readonly Regex QuestionPattern = new(
        @"^\s*(câu(\s*hỏi)?\s*)?(?<number>\d{1,3})\s*[.:)\-–—]\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Dòng đáp án: "A.", "b)", "(C)", "D -". Chỉ nhận A→H để không nuốt nhầm một câu bắt đầu bằng
    /// chữ cái đơn lẻ khác.</summary>
    private static readonly Regex AnswerPattern = new(
        @"^\s*[*✓✔]?\s*\(?\s*([a-hA-H])\s*[.):\-–—]\s*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Dòng ghi đáp án đúng của MỘT câu: "Đáp án: A", "ĐA: A,C", "Answer - B", "Key: D".</summary>
    private static readonly Regex CorrectLinePattern = new(
        @"^\s*(đáp\s*án(\s*đúng)?|đ\.?a|answer|key)\s*[:.\-–—]?\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExplanationPattern = new(
        @"^\s*(giải\s*thích|giải\s*nghĩa|explanation)\s*[:.\-–—]\s*(?<text>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Điểm ghi riêng cho một câu: "Điểm: 2".</summary>
    private static readonly Regex ScorePattern = new(
        @"^\s*(điểm|score)\s*[:.\-–—]\s*(?<score>\d+([.,]\d+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Dấu "(đúng)" viết ngay trong nội dung đáp án.</summary>
    private static readonly Regex InlineCorrectPattern = new(
        @"\s*[\(\[]\s*(đúng|correct|x)\s*[\)\]]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Một mục trong bảng đáp án: số câu, rồi tới các chữ cái đáp án ("1. A", "1-A", "12: A,C").</summary>
    private static readonly Regex KeyEntryPattern = new(
        @"(?<number>\d{1,3})\s*[.:)\-–—]?\s*(?<letter>[a-hA-H](\s*[,;/+&]\s*[a-hA-H])*)(?![a-zA-Z0-9À-ỹ])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Đáp án dính liền ngay trong một dòng: "...sau đây?A. Chất cháy B. Oxy C. Nguồn nhiệt".
    /// Bắt cả trường hợp không có khoảng trắng sau dấu chấm.</summary>
    private static readonly Regex InlineAnswerPattern = new(
        @"(?:^|[\s?!:;.,\-–—\)])(?<letter>[A-H])\s*[.)]\s*(?=[\p{L}\p{N}])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Dòng "Đáp án: A" bị dán dính vào cuối đáp án cuối cùng.</summary>
    private static readonly Regex TrailingKeyPattern = new(
        @"(?<=\S)\s*(?<key>(đáp\s*án(\s*đúng)?|answer|key)\s*[:.\-–—]\s*[a-hA-H](\s*[,;/]\s*[a-hA-H])*)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const int MaxQuestion = 2000;

    public WordQuestionDocument Parse(Stream docxStream)
    {
        using var memory = new MemoryStream();
        docxStream.CopyTo(memory);
        memory.Position = 0;

        using var document = WordprocessingDocument.Open(memory, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return new WordQuestionDocument(Array.Empty<ParsedQuestionGroup>(), Array.Empty<(int, string)>());

        var state = new ParseState();

        foreach (var element in body.Elements<OpenXmlElement>())
        {
            if (element is Paragraph paragraph) ReadParagraph(paragraph, state);
            else if (element is Table table) ReadTable(table, state);
        }

        state.FlushQuestion();
        state.FlushGroup();

        return state.Build();
    }

    // ==================================================================
    //  LƯỢT 1: ĐỌC TỪNG ĐOẠN VĂN
    // ==================================================================

    /// <summary>
    /// Một đoạn văn trong Word không phải lúc nào cũng là một dòng: người soạn xuống dòng bằng Shift+Enter
    /// thì cả câu hỏi lẫn bốn đáp án nằm chung một đoạn, và có file còn viết dính liền không xuống dòng gì
    /// cả. Nên trước khi xét vai của dòng, phải bẻ đoạn văn ra thành từng dòng thật.
    /// </summary>
    private static void ReadParagraph(Paragraph paragraph, ParseState state)
    {
        var heading = IsHeading(paragraph);

        foreach (var (segment, emphasized) in SplitByLineBreak(paragraph))
        {
            var lineList = SplitInlineAnswer(segment);

            // Đáp án dính liền nhau trong một dòng thì thông tin in đậm của cả dòng không còn chỉ đúng vào
            // một đáp án nào nữa — bỏ, tránh đánh dấu sai hàng loạt.
            var keepEmphasis = emphasized && lineList.Count == 1;

            foreach (var line in lineList)
            {
                state.Line++;
                ReadLine(line, keepEmphasis, heading, state);
            }
        }
    }

    /// <summary>Bẻ một đoạn văn theo ngắt dòng mềm (Shift+Enter). Kèm theo mỗi dòng là cờ "toàn bộ chữ của
    /// dòng được in đậm / gạch chân" — cách đánh dấu đáp án đúng phổ biến trong đề thi.</summary>
    private static List<(string Text, bool Emphasized)> SplitByLineBreak(Paragraph paragraph)
    {
        var resultList = new List<(string, bool)>();
        var builder = new System.Text.StringBuilder();
        var emphasizedLength = 0;
        var totalLength = 0;

        // Bôi vàng bằng cách tô nền CẢ ĐOẠN VĂN (Shading của pPr) chứ không phải từng mẩu chữ — Word ghi ra
        // chỗ khác hẳn, không đọc ở đây thì dưới kia không mẩu chữ nào có dấu hiệu gì.
        var paragraphShaded = IsShaded(paragraph.ParagraphProperties);

        void Flush()
        {
            var text = WordSectionDetector.NormalizeTitle(builder.ToString());
            // Tính theo TỈ LỆ chữ được nhấn mạnh chứ không đòi hỏi mọi mẩu chữ đều đậm: người soạn hay bôi
            // đậm mỗi phần nội dung đáp án mà để nguyên chữ cái đầu dòng ("B." thường, "114" đậm).
            if (text.Length > 0)
                resultList.Add((text, totalLength > 0 && emphasizedLength * 10 >= totalLength * 6));
            builder.Clear();
            emphasizedLength = 0;
            totalLength = 0;
        }

        foreach (var element in paragraph.Descendants().Where(x => x.LocalName is "t" or "br" or "cr" or "tab"))
        {
            switch (element.LocalName)
            {
                case "t":
                    builder.Append(element.InnerText);
                    var length = element.InnerText.Count(x => !char.IsWhiteSpace(x));
                    totalLength += length;
                    if (paragraphShaded || IsEmphasized(element.Ancestors<Run>().FirstOrDefault()))
                        emphasizedLength += length;
                    break;

                case "tab":
                    builder.Append(' ');
                    break;

                default:
                    Flush();
                    break;
            }
        }

        Flush();
        return resultList;
    }

    /// <summary>
    /// Tách một dòng viết dính: "…3 yếu tố nào sau đây?A. Chất cháy B. Oxy C. Nguồn nhiệt" thành câu hỏi và
    /// từng đáp án.
    ///
    /// Chỉ tách khi thấy từ HAI dấu đáp án trở lên và chúng chạy đúng thứ tự A, B, C… — bằng không, một câu
    /// có chứa "…theo mục A. của quy định" sẽ bị cắt làm đôi.
    /// </summary>
    private static List<string> SplitInlineAnswer(string text)
    {
        var single = new List<string> { text };

        var matchList = InlineAnswerPattern.Matches(text);
        if (matchList.Count < 2) return SplitTrailingKey(single);

        var pointList = new List<int>();
        var expected = 'A';
        foreach (Match match in matchList)
        {
            var letter = match.Groups["letter"].Value[0];
            if (letter != expected) continue;

            pointList.Add(match.Groups["letter"].Index);
            expected++;
        }

        if (pointList.Count < 2) return SplitTrailingKey(single);

        var resultList = new List<string>();
        var question = text[..pointList[0]].Trim();
        if (question.Length > 0) resultList.Add(question);

        for (var index = 0; index < pointList.Count; index++)
        {
            var end = index + 1 < pointList.Count ? pointList[index + 1] : text.Length;
            var part = text[pointList[index]..end].Trim();
            if (part.Length > 0) resultList.Add(part);
        }

        return SplitTrailingKey(resultList);
    }

    /// <summary>Gỡ phần "Đáp án: A" bị dán dính vào cuối một dòng thành một dòng riêng.</summary>
    private static List<string> SplitTrailingKey(List<string> lineList)
    {
        var resultList = new List<string>();
        foreach (var line in lineList)
        {
            var match = TrailingKeyPattern.Match(line);
            if (!match.Success)
            {
                resultList.Add(line);
                continue;
            }

            var head = line[..match.Index].Trim();
            if (head.Length > 0) resultList.Add(head);
            resultList.Add(match.Groups["key"].Value.Trim());
        }

        return resultList;
    }

    private static void ReadLine(string text, bool emphasized, bool heading, ParseState state)
    {
        if (text.Length == 0) return;

        // ---- Đang ở trong BẢNG ĐÁP ÁN cuối đề ----
        if (state.InKeyBlock && ReadKeyLine(text, state)) return;

        // 1. Dòng chia bài.
        if (IsGroupLine(text, heading))
        {
            state.FlushQuestion();
            state.FlushGroup();
            state.GroupTitle = text;
            return;
        }

        // 2. "ĐÁP ÁN" đứng một mình (hoặc "ĐÁP ÁN BÀI 2") = mở đầu bảng đáp án chung, không phải đáp án của
        //    một câu. Từ đây mọi dòng số + chữ cái đều là mục của bảng.
        var correctMatch = CorrectLinePattern.Match(text);
        if (correctMatch.Success)
        {
            var rest = correctMatch.Groups["rest"].Value.Trim();

            // Mở đầu BẢNG ĐÁP ÁN chung. Ba dạng đều phải nhận:
            //   "ĐÁP ÁN"                     → tiêu đề trơ, các mục nằm ở những dòng sau;
            //   "ĐÁP ÁN BÀI 2"               → tiêu đề kèm tên bài;
            //   "ĐÁP ÁN: 1A, 2B, 3C"         → tiêu đề VÀ các mục nằm ngay trên cùng một dòng.
            // Dạng thứ ba trước đây bị coi là tiêu đề trơ nên toàn bộ đáp án trên dòng đó rơi mất.
            var keyOnSameLine = KeyEntryPattern.Matches(rest);
            if (rest.Length == 0 || keyOnSameLine.Count > 0 || (rest.Length <= 60 && ParseLetterList(rest).Count == 0))
            {
                state.FlushQuestion();
                state.StartKeyBlock(rest.Length == 0 || keyOnSameLine.Count > 0 ? null : rest);
                if (keyOnSameLine.Count > 0) ReadKeyLine(rest, state);
                return;
            }

            // 3. Đáp án đúng của chính câu đang đọc: "Đáp án: A".
            //    Chỉ nhận khi phần còn lại NGẮN và có chữ cái đáp án — bằng không, một câu hỏi mở đầu bằng
            //    "Đáp án nào sau đây đúng?" (hay một đoạn văn bắt đầu bằng "Đa số...") sẽ bị nuốt mất.
            var letterList = ParseLetterList(rest);
            if (state.Question is not null && letterList.Count > 0 && rest.Length <= 30)
            {
                state.CorrectLetterList.AddRange(letterList);
                return;
            }
        }

        // 4. Dòng chỉ gồm các cặp "số câu + đáp án" ("1. A   2. B   3. C") là một mẩu bảng đáp án, dù phía
        //    trên không có dòng tiêu đề "ĐÁP ÁN" nào. Không thể nhầm với câu hỏi hay đáp án thường: cả dòng
        //    không còn chữ nghĩa gì ngoài các cặp đó.
        if (IsKeyOnlyLine(text))
        {
            state.FlushQuestion();
            if (!state.InKeyBlock) state.StartKeyBlock(null);
            ReadKeyLine(text, state);
            return;
        }

        // 5. Dòng giải thích / điểm.
        var explanationMatch = ExplanationPattern.Match(text);
        if (explanationMatch.Success && state.Question is not null)
        {
            state.Explanation = explanationMatch.Groups["text"].Value.Trim();
            return;
        }

        var scoreMatch = ScorePattern.Match(text);
        if (scoreMatch.Success && state.Question is not null)
        {
            if (decimal.TryParse(scoreMatch.Groups["score"].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture,
                    out var score))
                state.Score = score;
            return;
        }

        // 5. Dòng đáp án. Chỉ tính khi đang ở trong một câu hỏi — "A. Nội quy chung" đứng một mình ở đầu tài
        //    liệu là mục lục, không phải đáp án.
        var answerMatch = AnswerPattern.Match(text);
        if (answerMatch.Success && state.Question is not null)
        {
            var letter = answerMatch.Groups[1].Value.ToUpperInvariant();
            var content = text[answerMatch.Length..].Trim();
            var trimmed = text.TrimStart();
            var marked = trimmed.StartsWith('*') || trimmed.StartsWith('✓') || trimmed.StartsWith('✔');

            if (InlineCorrectPattern.IsMatch(content))
            {
                content = InlineCorrectPattern.Replace(content, string.Empty).Trim();
                marked = true;
            }

            if (content.Length == 0) return;

            state.AnswerList.Add(new AnswerDraft(letter, content, marked, emphasized));
            state.LastWasAnswer = true;
            return;
        }

        // 6. Câu hỏi mới: hoặc ghi rõ "Câu 1", hoặc là một đoạn chữ mới xuất hiện sau khi câu trước đã liệt kê
        //    xong đáp án (file dùng đánh số tự động của Word không có chữ "Câu" nào để bám vào).
        var questionMatch = QuestionPattern.Match(text);
        if (questionMatch.Success || (state.Question is not null && state.AnswerList.Count > 0))
        {
            state.FlushQuestion();
            state.Question = questionMatch.Success ? text[questionMatch.Length..].Trim() : text;
            state.QuestionNumber = questionMatch.Success
                ? int.Parse(questionMatch.Groups["number"].Value)
                : null;
            state.HasQuestionPrefix = questionMatch.Success;
            state.QuestionLine = state.Line;
            state.LastWasAnswer = false;
            return;
        }

        // 7. Không mang dấu hiệu nào: viết tiếp cho đáp án hoặc câu hỏi ngay trước đó.
        if (state.Question is null)
        {
            // Chưa có câu hỏi nào: giữ lại làm nội dung câu hỏi (file đánh số tự động, dòng đầu là câu hỏi).
            // Nếu hóa ra nó không có đáp án nào thì lượt kiểm tra cuối sẽ lặng lẽ bỏ đi — đó chỉ là câu dẫn
            // "Hướng dẫn: ..." mở đầu tài liệu.
            state.Question = text;
            state.QuestionNumber = null;
            state.HasQuestionPrefix = false;
            state.QuestionLine = state.Line;
            return;
        }

        if (state.LastWasAnswer && state.AnswerList.Count > 0)
        {
            var last = state.AnswerList[^1];
            state.AnswerList[^1] = last with { Content = last.Content + " " + text };
        }
        else
        {
            state.Question += " " + text;
        }
    }

    /// <summary>Dòng chia bài. Dòng đáp án ("A. Bài 2 giờ") và dòng câu hỏi ("Câu 3: Bài nào...") đã bị loại
    /// ngay từ hai điều kiện đầu, nên phần còn lại mở đầu bằng "Bài/Chương/Phần + số" thì đúng là dòng chia
    /// bài — kể cả khi nó nằm ngay sau danh sách đáp án của câu trước, vốn là chỗ nó luôn xuất hiện.</summary>
    private static bool IsGroupLine(string text, bool heading)
    {
        if (QuestionPattern.IsMatch(text)) return false;
        if (AnswerPattern.IsMatch(text)) return false;

        return GroupPattern.IsMatch(text) || heading;
    }

    /// <summary>Đọc một dòng trong bảng đáp án cuối đề. Trả về false nếu dòng này rõ ràng không còn thuộc
    /// bảng (một câu hỏi thật bắt đầu lại), để lượt quét chính xử lý tiếp.</summary>
    private static bool ReadKeyLine(string text, ParseState state)
    {
        // Dòng "Bài n" gặp ngay trong bảng đáp án mang hai nghĩa khác hẳn nhau, phân biệt bằng phần chữ phía
        // sau số: "BÀI 2" trơ trọi là đổi sang phần đáp án của bài 2 (bảng đáp án chia theo bài), còn
        // "BÀI 2: SƠ CẤP CỨU" có tên bài đầy đủ là đã hết bảng đáp án, sang phần câu hỏi của bài mới.
        var groupMatch = GroupPattern.Match(text);
        if (groupMatch.Success && !QuestionPattern.IsMatch(text))
        {
            if (text[groupMatch.Length..].Trim().Length > 0)
            {
                state.EndKeyBlock();
                return false;
            }

            state.KeyTargetTitle = text;
            return true;
        }

        // Bảng đáp án chỉ liệt kê chữ cái, không ghi số câu ("A", "B", "C" mỗi dòng một chữ, hoặc "A B C D"):
        // ghép theo đúng thứ tự câu trong phạm vi của bảng.
        if (Regex.IsMatch(text, @"^[a-hA-H]([\s,;/]+[a-hA-H])*$"))
        {
            foreach (var letter in ParseLetterList(text)) state.AddOrdinalKeyEntry(letter);
            return true;
        }

        var matchList = KeyEntryPattern.Matches(text);
        if (matchList.Count == 0)
        {
            // Dòng chữ thường xen giữa bảng ("Chúc các bạn làm bài tốt") — bỏ qua, vẫn coi là còn trong bảng.
            // Chỉ thoát khỏi bảng khi gặp một câu hỏi thật sự (có nội dung dài sau số thứ tự).
            var questionMatch = QuestionPattern.Match(text);
            if (questionMatch.Success && text[questionMatch.Length..].Trim().Length > 2)
            {
                state.EndKeyBlock();
                return false;
            }
            return true;
        }

        // Một dòng có thể chứa nhiều mục: "1. A   2. B   3. C".
        foreach (Match match in matchList)
        {
            var number = int.Parse(match.Groups["number"].Value);
            var letterList = ParseLetterList(match.Groups["letter"].Value);
            if (letterList.Count > 0) state.AddKeyEntry(number, letterList);
        }

        return true;
    }

    /// <summary>
    /// Bảng chỉ gồm số câu và chữ cái đáp án = BẢNG ĐÁP ÁN, đọc được cả hai cách trình bày thường gặp:
    ///
    ///   Câu | Đáp án        và        Câu | 1 | 2 | 3 | 4
    ///    1  |   A                      ĐA | A | B | A | C
    ///    2  |   B
    ///
    /// Kiểu nằm ngang không ghép được bằng cách nối cả hàng thành một dòng (hàng số và hàng chữ cái nằm
    /// tách nhau), nên phải ghép theo CỘT giữa hàng số và hàng chữ cái ngay dưới nó.
    /// </summary>
    private static bool TryReadKeyTable(Table table, ParseState state)
    {
        var rowList = table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>()
                .Select(cell => WordSectionDetector.NormalizeTitle(cell.InnerText))
                .ToList())
            .ToList();
        if (rowList.Count == 0) return false;

        var cellList = rowList.SelectMany(x => x).Where(x => x.Length > 0).ToList();
        if (cellList.Count == 0) return false;

        // Mỗi ô là số câu / chữ cái đáp án / chữ tiêu đề cột ("Câu", "Đáp án", "STT")...
        var cellByCell = cellList.Count >= 4 && cellList.All(IsKeyCell)
                         && cellList.Any(IsNumberCell) && cellList.Any(IsLetterCell);

        // ...hoặc mỗi Ô / mỗi HÀNG là một cặp viết liền ("1. A"), kiểu bảng đáp án 4-5 cột hay gặp.
        var rowByRow = rowList.All(row => row.All(cell => cell.Length == 0 || IsKeyOnlyLine(cell) || IsKeyCell(cell)))
                       && cellList.Count(IsKeyOnlyLine) >= 2;

        if (!cellByCell && !rowByRow) return false;

        if (rowByRow && !cellByCell)
        {
            if (!state.InKeyBlock) state.StartKeyBlock(null);
            foreach (var row in rowList)
            {
                state.Line++;
                foreach (var cell in row.Where(x => x.Length > 0)) ReadKeyLine(cell, state);
            }
            return true;
        }

        if (!state.InKeyBlock) state.StartKeyBlock(null);
        state.Line++;

        for (var index = 0; index < rowList.Count; index++)
        {
            var row = rowList[index];

            // Hai cột dọc: "1 | A".
            if (row.Count == 2 && IsNumberCell(row[0]) && IsLetterCell(row[1]))
            {
                state.AddKeyEntry(NumberOf(row[0]), ParseLetterList(row[1]));
                continue;
            }

            // Nằm ngang: hàng số, hàng chữ cái ngay dưới — ghép theo cột.
            if (index + 1 >= rowList.Count) continue;

            var below = rowList[index + 1];
            for (var column = 0; column < Math.Min(row.Count, below.Count); column++)
            {
                if (!IsNumberCell(row[column]) || !IsLetterCell(below[column])) continue;
                state.AddKeyEntry(NumberOf(row[column]), ParseLetterList(below[column]));
            }
        }

        return true;
    }

    private static bool IsKeyCell(string text) =>
        IsNumberCell(text) || IsLetterCell(text)
        || Regex.IsMatch(text, @"^(câu|c|stt|số|đáp\s*án|đ\.?a|answer|key|số\s*câu)\s*:?$", RegexOptions.IgnoreCase);

    private static bool IsNumberCell(string text) =>
        Regex.IsMatch(text, @"^(câu\s*)?\d{1,3}\s*[.:)]?$", RegexOptions.IgnoreCase);

    private static bool IsLetterCell(string text) =>
        Regex.IsMatch(text, @"^[a-hA-H](\s*[,;/+&]\s*[a-hA-H])*$");

    private static int NumberOf(string text) =>
        int.Parse(Regex.Match(text, @"\d{1,3}").Value);

    /// <summary>
    /// Dòng chỉ chứa các cặp "số câu + đáp án" và dấu ngăn, không còn chữ nghĩa nào khác: "1. A 2. B 3. C",
    /// "1A 2B", "Câu 1: A". Đây là một mẩu của bảng đáp án, kể cả khi người soạn không viết dòng "ĐÁP ÁN".
    ///
    /// Đòi hỏi ít nhất HAI cặp, hoặc một cặp mang chữ "Câu" phía trước — một dòng "1. A" trơ trọi cũng có thể
    /// là câu hỏi số 1 có nội dung vỏn vẹn chữ "A", hiếm nhưng không đáng liều.
    /// </summary>
    private static bool IsKeyOnlyLine(string text)
    {
        var matchList = KeyEntryPattern.Matches(text);
        if (matchList.Count == 0) return false;

        // Bỏ hết các cặp đã nhận ra, cộng thêm chữ "câu" và dấu ngăn; còn sót chữ nghĩa nghĩa là dòng này
        // có nội dung thật, không phải mẩu bảng đáp án.
        var rest = KeyEntryPattern.Replace(text, " ");
        // Nhãn đứng đầu ("ĐÁP ÁN: 1A, 2B, 3C") không làm dòng này hết là bảng đáp án.
        rest = Regex.Replace(rest, @"đáp\s*án(\s*đúng)?|answer\s*key|answer|key|đ\.?a|c[âa]u|[\s.,;:/|\-–—()+&]",
            " ", RegexOptions.IgnoreCase);
        if (rest.Trim().Length > 0) return false;

        return matchList.Count >= 2 || Regex.IsMatch(text, @"^\s*c[âa]u\s*\d", RegexOptions.IgnoreCase);
    }

    /// <summary>Bảng trong tài liệu: hoặc là bảng câu hỏi soạn theo file mẫu, hoặc là BẢNG ĐÁP ÁN cuối đề
    /// (hai cột số câu / chữ cái, hoặc một hàng số và một hàng chữ cái).</summary>
    private static void ReadTable(Table table, ParseState state)
    {
        state.FlushQuestion();

        if (TryReadKeyTable(table, state)) return;

        if (state.InKeyBlock)
        {
            foreach (var row in table.Elements<TableRow>())
            {
                state.Line++;
                var cellList = row.Elements<TableCell>()
                    .Select(cell => WordSectionDetector.NormalizeTitle(cell.InnerText))
                    .ToList();

                // Hai cột "số câu | đáp án" là kiểu phổ biến nhất; ghép các ô lại rồi dò bằng cùng một mẫu
                // cũng đọc được kiểu một hàng số, một hàng chữ cái.
                ReadKeyLine(string.Join(' ', cellList), state);
            }
            return;
        }

        var rowIndex = 0;
        foreach (var row in table.Elements<TableRow>())
        {
            state.Line++;
            rowIndex++;

            var cellList = row.Elements<TableCell>()
                .Select(cell => WordSectionDetector.NormalizeTitle(cell.InnerText))
                .ToList();
            if (cellList.Count < 4) continue;

            // Dòng tiêu đề cột của file mẫu.
            if (rowIndex == 1 && cellList[0].Contains("nội dung", StringComparison.OrdinalIgnoreCase)) continue;
            if (cellList[0].Length == 0) continue;

            var answerList = new List<AnswerDraft>();
            for (var column = 3; column < Math.Min(8, cellList.Count); column++)
            {
                if (cellList[column].Length == 0) continue;
                answerList.Add(new AnswerDraft(((char)('A' + column - 3)).ToString(), cellList[column], false, false));
            }

            var correctList = cellList.Count > 8 ? ParseLetterList(cellList[8]) : new List<string>();
            var explanation = cellList.Count > 9 && cellList[9].Length > 0 ? cellList[9] : null;
            decimal? score = cellList.Count > 2 && decimal.TryParse(cellList[2], out var parsed) && parsed > 0 ? parsed : null;

            state.AddDraft(new QuestionDraft(
                null, cellList[0], answerList, correctList, explanation, score, state.Line, true));
        }
    }

    // ==================================================================
    //  ĐỊNH DẠNG CHỮ
    // ==================================================================

    private static bool IsHeading(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return styleId is not null
               && (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                   || styleId.Equals("Title", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Chữ in đậm, gạch chân hoặc TÔ NỀN — những cách đánh dấu đáp án đúng phổ biến nhất trong đề
    /// thi soạn bằng Word. Xét theo từng mẩu chữ (run); cả dòng đều được nhấn mạnh thì mới coi là đánh dấu,
    /// để không nhầm với một cụm từ khóa bôi đậm giữa câu (xem SplitByLineBreak).</summary>
    private static bool IsEmphasized(Run? run)
    {
        if (run is null) return false;
        return IsOn(run.RunProperties?.Bold) || IsOn(run.RunProperties?.Underline)
               || IsShaded(run.RunProperties);

        static bool IsOn(OpenXmlElement? property) =>
            property is not null && WordValue.Attr(property, "val") is not ("0" or "false" or "none");
    }

    /// <summary>
    /// Chữ được tô nền. Hai cách tô trong Word ghi ra hai chỗ khác nhau nên phải đọc cả hai: bút bôi màu
    /// (highlight, ghi bằng TÊN màu) và nền ô chữ / nền đoạn văn (shd, ghi bằng MÃ màu) — người soạn dùng
    /// lẫn lộn, cùng một vệt vàng trên màn hình có thể ra một trong hai.
    ///
    /// Nền trắng không tính là đánh dấu: đó là nền mặc định, nhiều file mang sẵn nó ở mọi dòng do chép từ
    /// nguồn khác về.
    /// </summary>
    private static bool IsShaded(OpenXmlElement? properties)
    {
        if (properties is null) return false;

        var highlight = WordValue.Attr(WordValue.Child(properties, "highlight"), "val");
        if (WordValue.ColorFromHighlightName(highlight) is string color && color != "FFFFFF") return true;

        var fill = WordValue.Attr(WordValue.Child(properties, "shd"), "fill");
        return WordValue.IsRealColor(fill) && !fill!.Equals("FFFFFF", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseLetterList(string value) =>
        value.Split(new[] { ',', ';', '/', '+', '&', ' ', '.', 'v', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x.Length == 1 && x[0] is >= 'A' and <= 'H')
            .Distinct()
            .ToList();

    // ==================================================================
    //  BẢN NHÁP VÀ LƯỢT 2: GHÉP ĐÁP ÁN, KIỂM TRA
    // ==================================================================

    private sealed record AnswerDraft(string Letter, string Content, bool Marked, bool Emphasized);

    /// <param name="Number">Số câu ghi trong tài liệu ("Câu 7" → 7), null nếu tài liệu đánh số tự động.</param>
    /// <param name="HasQuestionPrefix">true = dòng này mở đầu bằng "Câu n" hoặc "n." rõ ràng. Dùng để phân
    /// biệt một câu hỏi thiếu đáp án (đáng báo lỗi) với một đoạn văn dẫn nhập (lặng lẽ bỏ qua).</param>
    private sealed record QuestionDraft(
        int? Number, string Content, List<AnswerDraft> AnswerList, List<string> CorrectLetterList,
        string? Explanation, decimal? Score, int Line, bool HasQuestionPrefix);

    private sealed record GroupDraft(string? Title, List<QuestionDraft> QuestionList);

    /// <param name="TargetTitle">Tên bài mà bảng đáp án này dành cho ("ĐÁP ÁN BÀI 2"), null = cho cả tài liệu
    /// hoặc cho bài đang đọc dở.</param>
    /// <param name="OrdinalList">Các đáp án liệt kê KHÔNG kèm số câu ("A", "B", "C" mỗi dòng một chữ) —
    /// ghép theo đúng thứ tự câu trong phạm vi của bảng.</param>
    private sealed record KeyBlock(
        string? TargetTitle, int GroupIndex, Dictionary<int, List<string>> EntryMap, List<List<string>> OrdinalList);

    private sealed class ParseState
    {
        private readonly List<GroupDraft> _groupList = new();
        private readonly List<KeyBlock> _keyBlockList = new();
        private readonly List<(int Line, string Message)> _warningList = new();

        public string? GroupTitle { get; set; }
        private readonly List<QuestionDraft> _questionList = new();

        public string? Question { get; set; }
        public int? QuestionNumber { get; set; }
        public bool HasQuestionPrefix { get; set; }
        public int QuestionLine { get; set; }
        public List<AnswerDraft> AnswerList { get; } = new();
        public List<string> CorrectLetterList { get; } = new();
        public string? Explanation { get; set; }
        public decimal? Score { get; set; }
        public bool LastWasAnswer { get; set; }
        public int Line { get; set; }

        public bool InKeyBlock => _currentKey is not null;
        private KeyBlock? _currentKey;

        public string? KeyTargetTitle
        {
            set
            {
                if (_currentKey is null) return;
                // Bảng đáp án chia theo bài: mỗi lần gặp "BÀI n" là mở một khối mới nhắm vào bài đó.
                _currentKey = new KeyBlock(value, _groupList.Count, new Dictionary<int, List<string>>(), new List<List<string>>());
                _keyBlockList.Add(_currentKey);
            }
        }

        public void StartKeyBlock(string? targetTitle)
        {
            _currentKey = new KeyBlock(targetTitle, _groupList.Count, new Dictionary<int, List<string>>(), new List<List<string>>());
            _keyBlockList.Add(_currentKey);
        }

        public void EndKeyBlock() => _currentKey = null;

        public void AddKeyEntry(int number, List<string> letterList)
        {
            if (_currentKey is null) return;
            _currentKey.EntryMap[number] = letterList;
        }

        /// <summary>Đáp án không kèm số câu — xếp vào hàng đợi, lát nữa ghép theo thứ tự câu.</summary>
        public void AddOrdinalKeyEntry(string letter)
        {
            _currentKey?.OrdinalList.Add(new List<string> { letter });
        }

        public void FlushQuestion()
        {
            if (Question is not null)
                AddDraft(new QuestionDraft(
                    QuestionNumber, Question, AnswerList.ToList(), CorrectLetterList.ToList(),
                    Explanation, Score, QuestionLine, HasQuestionPrefix));

            Question = null;
            QuestionNumber = null;
            HasQuestionPrefix = false;
            AnswerList.Clear();
            CorrectLetterList.Clear();
            Explanation = null;
            Score = null;
            LastWasAnswer = false;
        }

        public void AddDraft(QuestionDraft draft)
        {
            if (_questionList.Count + _groupList.Sum(x => x.QuestionList.Count) >= MaxQuestion) return;
            _questionList.Add(draft);
        }

        public void FlushGroup()
        {
            // Giữ cả nhóm rỗng ra khỏi danh sách, nhưng vẫn phải đẩy nhóm đang có câu hỏi vào trước khi đổi
            // tên nhóm — nếu không, câu của bài trước rơi sang bài sau.
            if (_questionList.Count > 0)
                _groupList.Add(new GroupDraft(GroupTitle, _questionList.ToList()));

            _questionList.Clear();
            GroupTitle = null;
            EndKeyBlock();
        }

        /// <summary>Lượt 2: ghép bảng đáp án vào từng câu rồi kiểm tra.</summary>
        public WordQuestionDocument Build()
        {
            ApplyKeyBlockList();

            var groupList = new List<ParsedQuestionGroup>();
            foreach (var group in _groupList)
            {
                var questionList = new List<ParsedQuestion>();
                foreach (var draft in group.QuestionList)
                    if (Validate(draft) is ParsedQuestion question) questionList.Add(question);

                if (questionList.Count > 0) groupList.Add(new ParsedQuestionGroup(group.Title, questionList));
            }

            return new WordQuestionDocument(groupList, _warningList);
        }

        /// <summary>
        /// Ghép từng bảng đáp án vào đúng nhóm câu hỏi của nó: theo tên bài ghi trên bảng ("ĐÁP ÁN BÀI 2"),
        /// không có thì theo nhóm mà bảng nằm ngay sau, cuối cùng mới rải cho cả tài liệu (bảng đáp án chung
        /// đặt ở cuối đề).
        ///
        /// Trong phạm vi đã chọn: khớp theo SỐ CÂU ghi trong tài liệu; tài liệu đánh số tự động (không có số
        /// để khớp) thì khớp theo thứ tự câu.
        /// </summary>
        private void ApplyKeyBlockList()
        {
            foreach (var block in _keyBlockList)
            {
                if (block.EntryMap.Count == 0 && block.OrdinalList.Count == 0) continue;

                var scopeList = ResolveScope(block);
                if (scopeList.Count == 0) continue;

                // Bảng chỉ liệt kê chữ cái: câu thứ n trong phạm vi lấy đáp án thứ n.
                for (var index = 0; index < block.OrdinalList.Count && index < scopeList.Count; index++)
                {
                    var target = scopeList[index];
                    if (target.CorrectLetterList.Count == 0) target.CorrectLetterList.AddRange(block.OrdinalList[index]);
                }

                foreach (var (number, letterList) in block.EntryMap)
                {
                    var target = scopeList.FirstOrDefault(x => x.Number == number)
                                 ?? (number >= 1 && number <= scopeList.Count && scopeList.All(x => x.Number is null)
                                     ? scopeList[number - 1]
                                     : null);

                    // Câu đã tự ghi đáp án đúng ngay dưới câu hỏi thì giữ nguyên, không để bảng đè lên.
                    if (target is null || target.CorrectLetterList.Count > 0) continue;

                    target.CorrectLetterList.AddRange(letterList);
                }
            }
        }

        /// <summary>
        /// Bảng đáp án này dành cho những câu nào.
        ///
        /// Bảng ghi rõ tên bài ("ĐÁP ÁN BÀI 2") thì theo tên. Không ghi thì phải đoán giữa hai khả năng: bảng
        /// của riêng bài vừa đọc xong, hay bảng chung cả đề đặt ở cuối. Chọn bằng cách thử cả hai và lấy bên
        /// khớp được NHIỀU số câu hơn — đề đánh số liên tục 1..40 sẽ khớp toàn tài liệu, còn đề đánh số lại
        /// từ 1 ở mỗi bài thì chỉ khớp trong bài của nó.
        /// </summary>
        private List<QuestionDraft> ResolveScope(KeyBlock block)
        {
            var allList = _groupList.SelectMany(x => x.QuestionList).ToList();

            if (block.TargetTitle is not null)
            {
                var named = _groupList.FirstOrDefault(x => SameTitle(x.Title, block.TargetTitle));
                if (named is not null) return named.QuestionList;
            }

            // Nhóm đang đọc dở lúc gặp bảng: sau khi chốt lại, nhóm đó nằm đúng ở vị trí GroupIndex.
            if (block.GroupIndex >= _groupList.Count) return allList;

            var current = _groupList[block.GroupIndex].QuestionList;
            return MatchCount(block, current) >= MatchCount(block, allList) ? current : allList;
        }

        private static int MatchCount(KeyBlock block, List<QuestionDraft> scopeList) =>
            block.EntryMap.Keys.Count(number => scopeList.Any(x => x.Number == number));

        /// <summary>So tên bài của bảng đáp án với tên nhóm. "ĐÁP ÁN BÀI 2" chỉ ghi "BÀI 2" trong khi nhóm
        /// tên đầy đủ là "BÀI 2: SƠ CẤP CỨU", nên so theo phần đầu chứ không đòi trùng khít.</summary>
        private static bool SameTitle(string? groupTitle, string? keyTitle)
        {
            if (groupTitle is null || keyTitle is null) return false;

            var group = WordSectionDetector.NormalizeTitle(groupTitle);
            var key = WordSectionDetector.NormalizeTitle(keyTitle);
            return group.StartsWith(key, StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith(group, StringComparison.OrdinalIgnoreCase);
        }

        private ParsedQuestion? Validate(QuestionDraft draft)
        {
            // Đoạn văn dẫn nhập ("Hướng dẫn: ...", lời tựa) lọt vào đây vì nó cũng là một đoạn chữ đứng riêng.
            // Không có đáp án nào VÀ không mở đầu bằng "Câu n" thì nó không phải câu hỏi — bỏ lặng lẽ, báo lỗi
            // chỉ làm người soạn hoang mang đi tìm một câu hỏi không tồn tại.
            if (draft.AnswerList.Count == 0 && !draft.HasQuestionPrefix) return null;

            if (draft.AnswerList.Count < 2)
            {
                _warningList.Add((draft.Line, $"Bỏ qua \"{Shorten(draft.Content)}\": câu hỏi phải có ít nhất 2 đáp án."));
                return null;
            }

            var correctSet = draft.CorrectLetterList.ToHashSet();
            if (correctSet.Count == 0)
                correctSet = draft.AnswerList.Where(x => x.Marked).Select(x => x.Letter).ToHashSet();

            // In đậm chỉ có nghĩa khi nó phân biệt được: cả bốn đáp án cùng in đậm là kiểu trình bày, không
            // phải đánh dấu đáp án đúng.
            if (correctSet.Count == 0)
            {
                var emphasizedList = draft.AnswerList.Where(x => x.Emphasized).ToList();
                if (emphasizedList.Count > 0 && emphasizedList.Count < draft.AnswerList.Count)
                    correctSet = emphasizedList.Select(x => x.Letter).ToHashSet();
            }

            // Không tìm ra đáp án đúng thì VẪN NHẬN câu hỏi, chỉ đánh dấu là cần bổ sung. Người soạn tick
            // lại đáp án đúng ngay trong ngân hàng câu hỏi, nhanh hơn nhiều so với việc sửa file Word rồi
            // nhập lại từ đầu — và quan trọng hơn: phần nội dung đã gõ không bị mất.
            if (correctSet.Count == 0)
                return new ParsedQuestion(
                    draft.Content.Trim(),
                    draft.AnswerList.Select(x => new ParsedAnswer(x.Content, false)).ToList(),
                    draft.Explanation, draft.Score, draft.Line, NeedsAnswerReview: true);

            var unknown = correctSet.FirstOrDefault(x => draft.AnswerList.All(a => a.Letter != x));
            if (unknown is not null)
            {
                _warningList.Add((draft.Line,
                    $"Bỏ qua \"{Shorten(draft.Content)}\": đáp án đúng \"{unknown}\" không có trong danh sách đáp án."));
                return null;
            }

            return new ParsedQuestion(
                draft.Content.Trim(),
                draft.AnswerList.Select(x => new ParsedAnswer(x.Content, correctSet.Contains(x.Letter))).ToList(),
                draft.Explanation, draft.Score, draft.Line);
        }

        private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "...";
    }
}
