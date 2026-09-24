// File: Infrastructure/Documents/WordTemplateBuilder.cs
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HbELearning.Infrastructure.Documents;

/// <summary>
/// Sinh file mẫu nhập liệu dạng Word (.docx) — cùng bộ cột và cùng dòng ví dụ với file mẫu Excel, chỉ khác
/// vỏ đựng. Có mặt cho những người soạn nội dung quen gõ trong Word hơn là điền vào bảng tính.
///
/// Bảng dữ liệu luôn là BẢNG ĐẦU TIÊN của tài liệu, phần hướng dẫn phía dưới cố ý viết bằng đoạn văn chứ
/// không phải bảng — ImportSourceReader lấy đúng bảng đầu tiên làm dữ liệu (xem ReadWord).
/// </summary>
public static class WordTemplateBuilder
{
    private const string HeaderFill = "2563EB";   // xanh, khớp màu tiêu đề của file mẫu Excel
    private const string SampleFill = "FEF3C7";   // vàng nhạt, đánh dấu dòng ví dụ cần xóa

    public static byte[] Build(
        string title,
        string[] headerList,
        string[][] sampleList,
        (string Field, string Note)[] guideList)
    {
        using var memory = new MemoryStream();

        using (var document = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document))
        {
            var body = new Body();

            body.Append(HeadingParagraph(title, 30, HeaderFill));
            body.Append(TextParagraph(
                "Điền dữ liệu vào bảng bên dưới, mỗi dòng là một bản ghi. Giữ nguyên dòng tiêu đề và "
                + "XÓA các dòng ví dụ (nền vàng) trước khi tải lên.", 20, italic: true));
            body.Append(EmptyParagraph());

            body.Append(BuildTable(headerList, sampleList));

            body.Append(EmptyParagraph());
            body.Append(HeadingParagraph("HƯỚNG DẪN NHẬP LIỆU", 26, HeaderFill));
            foreach (var (field, note) in guideList)
            {
                if (field.Length == 0 && note.Length == 0) { body.Append(EmptyParagraph()); continue; }
                body.Append(GuideParagraph(field, note));
            }

            // Trang ngang: bảng câu hỏi có tới 10 cột, để dọc thì mỗi ô còn vài ký tự bề ngang.
            body.Append(new SectionProperties(new PageSize
            {
                Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape
            }));

            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return memory.ToArray();
    }

    // ==================================================================
    //  BẢNG DỮ LIỆU
    // ==================================================================

    private static Table BuildTable(string[] headerList, string[][] sampleList)
    {
        var table = new Table(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "D4D4D8" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "D4D4D8" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "D4D4D8" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "D4D4D8" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "D4D4D8" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "D4D4D8" }),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));

        var headerRow = new TableRow();
        foreach (var header in headerList)
            headerRow.Append(BuildCell(header, HeaderFill, bold: true, color: "FFFFFF"));
        table.Append(headerRow);

        foreach (var sample in sampleList)
        {
            var row = new TableRow();
            for (var i = 0; i < headerList.Length; i++)
                row.Append(BuildCell(i < sample.Length ? sample[i] : string.Empty, SampleFill));
            table.Append(row);
        }

        // Ba dòng trắng sẵn để gõ ngay, khỏi phải tự thêm dòng vào bảng.
        for (var i = 0; i < 3; i++)
        {
            var row = new TableRow();
            for (var c = 0; c < headerList.Length; c++) row.Append(BuildCell(string.Empty, null));
            table.Append(row);
        }

        return table;
    }

    private static TableCell BuildCell(string text, string? fill, bool bold = false, string? color = null)
    {
        var runProperties = new RunProperties(new FontSize { Val = "18" });
        if (bold) runProperties.Append(new Bold());
        if (color is not null) runProperties.Append(new Color { Val = color });

        var cellProperties = new TableCellProperties(
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
        if (fill is not null)
            cellProperties.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = fill });

        return new TableCell(cellProperties, new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "40", After = "40" }),
            new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
    }

    // ==================================================================
    //  ĐOẠN VĂN
    // ==================================================================

    private static Paragraph HeadingParagraph(string text, int halfPointSize, string color) =>
        new(new ParagraphProperties(new SpacingBetweenLines { After = "120" }),
            new Run(
                new RunProperties(new Bold(), new FontSize { Val = halfPointSize.ToString() }, new Color { Val = color }),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph TextParagraph(string text, int halfPointSize, bool italic = false)
    {
        var runProperties = new RunProperties(new FontSize { Val = halfPointSize.ToString() });
        if (italic) runProperties.Append(new Italic());

        return new Paragraph(new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    /// <summary>Một dòng hướng dẫn: tên cột in đậm, rồi tới phần giải thích.</summary>
    private static Paragraph GuideParagraph(string field, string note)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }));

        if (field.Length > 0)
            paragraph.Append(new Run(
                new RunProperties(new Bold(), new FontSize { Val = "20" }),
                new Text(field + ": ") { Space = SpaceProcessingModeValues.Preserve }));

        paragraph.Append(new Run(
            new RunProperties(new FontSize { Val = "20" }),
            new Text(note) { Space = SpaceProcessingModeValues.Preserve }));

        return paragraph;
    }

    private static Paragraph EmptyParagraph() => new(new Run(new Text(string.Empty)));
}
