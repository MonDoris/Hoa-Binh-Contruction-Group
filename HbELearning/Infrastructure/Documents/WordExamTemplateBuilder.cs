// File: Infrastructure/Documents/WordExamTemplateBuilder.cs
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HbELearning.Infrastructure.Documents;

/// <summary>
/// Sinh file mẫu Word dạng ĐỀ SOẠN TỰ DO: dòng chia bài, "Câu 1:", các đáp án "A. B. C. D.", đáp án đúng
/// được đánh dấu — đúng thứ mà WordQuestionParser đọc và cũng đúng thứ người soạn đề vẫn gõ sẵn.
///
/// Khác <see cref="WordTemplateBuilder"/> (bảng có sẵn cột, dành cho lối nhập đọc bảng): ở đây không có cột
/// nào cả. Người soạn gõ tiếp vào chính file này, hoặc dán đề có sẵn của mình vào, là nhập được.
///
/// Mỗi câu ví dụ đánh dấu đáp án đúng bằng MỘT kiểu khác nhau (tô nền, in đậm, dấu *, dòng "Đáp án: B") để
/// người soạn thấy tận mắt kiểu nào cũng nhận được, khỏi phải sửa lại cả đề theo một kiểu duy nhất.
/// </summary>
public static class WordExamTemplateBuilder
{
    private const string TitleColor = "2563EB";   // xanh, khớp màu tiêu đề của file mẫu Excel
    private const string NoteColor = "B45309";    // nâu cam, phần dặn dò cần xóa trước khi nhập
    private const string SectionColor = "111827";

    public static byte[] Build(ExamTemplate template)
    {
        using var memory = new MemoryStream();

        using (var document = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document))
        {
            var body = new Body();

            body.Append(Heading(template.Title, 30, TitleColor));
            foreach (var note in template.NoteList) body.Append(Note(note));
            body.Append(Empty());

            foreach (var section in template.SectionList)
            {
                if (section.Title is not null) body.Append(Heading(section.Title, 26, SectionColor));

                var number = 1;
                foreach (var question in section.QuestionList) AppendQuestion(body, number++, question);

                body.Append(Empty());
            }

            body.Append(Heading("HƯỚNG DẪN", 26, TitleColor));
            foreach (var (field, note) in template.Guide)
            {
                if (field.Length == 0 && note.Length == 0) { body.Append(Empty()); continue; }
                body.Append(Guide(field, note));
            }

            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return memory.ToArray();
    }

    /// <summary>Một câu ví dụ: dòng câu hỏi, các dòng đáp án, rồi dòng đáp án đúng / giải thích / điểm nếu có.</summary>
    private static void AppendQuestion(Body body, int number, ExamSampleQuestion question)
    {
        body.Append(Line($"Câu {number}: {question.Content}", bold: true));

        for (var i = 0; i < question.AnswerList.Length; i++)
        {
            var isCorrect = question.CorrectIndexList.Contains(i);
            var letter = (char)('A' + i);

            // Dấu * đứng TRƯỚC chữ cái đáp án, đúng cách người soạn hay gõ: "*C. Phòng server...".
            var star = isCorrect && question.Mark == ExamAnswerMark.Star ? "*" : string.Empty;

            body.Append(Line($"{star}{letter}. {question.AnswerList[i]}",
                bold: isCorrect && question.Mark == ExamAnswerMark.Bold,
                highlight: isCorrect && question.Mark == ExamAnswerMark.Highlight));
        }

        if (question.Mark == ExamAnswerMark.KeyLine)
        {
            var letterList = question.CorrectIndexList.Select(x => ((char)('A' + x)).ToString());
            body.Append(Line("Đáp án: " + string.Join(", ", letterList)));
        }

        if (question.Explanation is not null) body.Append(Line("Giải thích: " + question.Explanation));
        if (question.Score is not null) body.Append(Line("Điểm: " + question.Score));

        body.Append(Empty());
    }

    // ==================================================================
    //  ĐOẠN VĂN
    // ==================================================================

    private static Paragraph Heading(string text, int halfPointSize, string color) =>
        new(new ParagraphProperties(new SpacingBetweenLines { Before = "160", After = "80" }),
            new Run(
                new RunProperties(new Bold(), new FontSize { Val = halfPointSize.ToString() }, new Color { Val = color }),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    /// <summary>
    /// Một dòng của đề.
    ///
    /// Tô nền bằng <c>highlight</c> (bút bôi màu) chứ không phải <c>shd</c>: đây là thứ nút bút bôi màu trên
    /// thanh công cụ Word sinh ra, nên người soạn bôi thêm cho những câu mình gõ tiếp sẽ ra đúng cùng một
    /// dạng với các câu ví dụ có sẵn.
    /// </summary>
    private static Paragraph Line(string text, bool bold = false, bool highlight = false)
    {
        var runProperties = new RunProperties(new FontSize { Val = "24" });
        if (bold) runProperties.Append(new Bold());
        if (highlight) runProperties.Append(new Highlight { Val = HighlightColorValues.Yellow });

        return new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "276", LineRule = LineSpacingRuleValues.Auto }),
            new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    /// <summary>Dòng dặn dò dưới tiêu đề. Mở đầu bằng dấu chấm tròn để bộ đọc đề không bắt nhầm nó thành câu
    /// hỏi hay đáp án — mọi khuôn dạng mà bộ đọc nhận đều đòi dòng phải MỞ ĐẦU bằng "Câu n", một chữ cái hay
    /// chữ "Đáp án".</summary>
    private static Paragraph Note(string text) =>
        new(new ParagraphProperties(new SpacingBetweenLines { After = "40" }),
            new Run(
                new RunProperties(new Italic(), new FontSize { Val = "20" }, new Color { Val = NoteColor }),
                new Text("• " + text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph Guide(string field, string note)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }));

        paragraph.Append(field.Length > 0
            ? new Run(
                new RunProperties(new Bold(), new FontSize { Val = "20" }),
                new Text("• " + field + ": ") { Space = SpaceProcessingModeValues.Preserve })
            : new Run(
                new RunProperties(new FontSize { Val = "20" }),
                new Text("• ") { Space = SpaceProcessingModeValues.Preserve }));

        paragraph.Append(new Run(
            new RunProperties(new FontSize { Val = "20" }),
            new Text(note) { Space = SpaceProcessingModeValues.Preserve }));

        return paragraph;
    }

    private static Paragraph Empty() => new(new Run(new Text(string.Empty)));
}
