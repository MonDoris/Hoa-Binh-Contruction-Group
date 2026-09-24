// File: Infrastructure/Import/ImportSourceReader.cs
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace HbELearning.Infrastructure.Import;

/// <summary>
/// Đọc file người dùng tải lên (Excel / Word / PDF) thành danh sách dòng đã tách ô (<see cref="ImportRow"/>).
/// Chỉ làm đúng việc bóc tách; mọi kiểm tra hợp lệ và ghi database vẫn nằm ở ExcelImportService.
/// </summary>
public static class ImportSourceReader
{
    /// <summary>Ký tự ngăn cột trong file PDF/Word dạng văn bản. Chọn gạch đứng vì nó gần như không bao giờ
    /// xuất hiện trong tiêu đề bài học hay nội dung câu hỏi, khác hẳn dấu phẩy hay tab.</summary>
    public const char ColumnSeparator = '|';

    public static List<ImportRow> Read(Stream stream, ImportFormat format) => format switch
    {
        ImportFormat.Word => ReadWord(stream),
        ImportFormat.Pdf => ReadPdf(stream),
        _ => ReadExcel(stream)
    };

    // ==================================================================
    //  EXCEL
    // ==================================================================

    private static List<ImportRow> ReadExcel(Stream stream)
    {
        var result = new List<ImportRow>();

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        var lastUsed = sheet.LastRowUsed();
        if (lastUsed is null) return result;

        // Bỏ dòng 1 (tiêu đề cột). Lấy số cột theo vùng đã dùng của cả sheet chứ không theo từng dòng:
        // dòng nào bỏ trống mấy ô cuối vẫn phải trả về đủ số ô để Text(cột) không bị lệch.
        var columnCount = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var r = 2; r <= lastUsed.RowNumber(); r++)
        {
            var row = sheet.Row(r);
            if (row.IsEmpty()) continue;

            var cellList = new string[columnCount];
            for (var c = 1; c <= columnCount; c++) cellList[c - 1] = row.Cell(c).GetString();

            result.Add(new ImportRow(r, cellList));
        }

        return result;
    }

    // ==================================================================
    //  WORD (.docx)
    // ==================================================================

    /// <summary>
    /// Lấy BẢNG ĐẦU TIÊN trong tài liệu, bỏ dòng đầu (tiêu đề cột). File mẫu Word do hệ thống sinh ra luôn
    /// đặt bảng dữ liệu lên trước phần hướng dẫn nên quy ước "bảng đầu tiên" là đủ; phần hướng dẫn phía dưới
    /// cố ý viết bằng đoạn văn, không phải bảng, để không bị nhầm là dữ liệu.
    ///
    /// Không có bảng nào thì lùi về đọc từng đoạn văn ngăn bằng dấu gạch đứng — cho người tự gõ một file
    /// Word trắng theo đúng cách viết của bản PDF.
    /// </summary>
    private static List<ImportRow> ReadWord(Stream stream)
    {
        // OpenXml cần stream tua lại được; stream của IFormFile thì có, nhưng file tải lên qua proxy đôi khi
        // chỉ đọc tiến được một lần — sao ra bộ nhớ cho chắc, file mẫu chỉ vài chục KB.
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;

        using var document = WordprocessingDocument.Open(memory, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return new List<ImportRow>();

        var table = body.Elements<Table>().FirstOrDefault();
        if (table is not null) return ReadWordTable(table);

        var lineList = body.Descendants<Paragraph>().Select(p => p.InnerText);
        return ReadSeparatedLine(lineList);
    }

    private static List<ImportRow> ReadWordTable(Table table)
    {
        var result = new List<ImportRow>();
        var rowNumber = 1;

        foreach (var row in table.Elements<TableRow>())
        {
            rowNumber++;
            if (rowNumber == 2) continue; // dòng tiêu đề cột

            // Mỗi ô có thể chứa nhiều đoạn văn (người dùng xuống dòng trong ô) — nối lại bằng xuống dòng
            // thay vì dán liền, không thì hai đoạn dính vào nhau thành một chuỗi vô nghĩa.
            var cellList = row.Elements<TableCell>()
                .Select(cell => string.Join('\n', cell.Elements<Paragraph>().Select(p => p.InnerText)).Trim())
                .ToArray();

            var importRow = new ImportRow(rowNumber, cellList);
            if (!importRow.IsEmpty) result.Add(importRow);
        }

        return result;
    }

    // ==================================================================
    //  PDF
    // ==================================================================

    /// <summary>
    /// PDF không có khái niệm ô hay bảng — chỉ có chữ đặt ở toạ độ. Nên quy ước: mỗi dòng là một bản ghi,
    /// các cột ngăn nhau bằng dấu gạch đứng, đúng như hướng dẫn in trong file mẫu. Dòng nào không có dấu
    /// gạch đứng thì coi là chữ trang trí (tiêu đề, ghi chú) và bỏ qua.
    /// </summary>
    private static List<ImportRow> ReadPdf(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;

        var lineList = new List<string>();
        using (var document = PdfDocument.Open(memory))
        {
            foreach (var page in document.GetPages())
            {
                // Ghép theo TỪ chứ không lấy thẳng Page.Text: Page.Text nối các ký tự lại mà không chèn dấu
                // cách, chữ tiếng Việt sẽ dính thành một khối (xem DocumentProcessingService cũng làm vậy).
                var builder = new StringBuilder();
                var lastY = double.NaN;

                foreach (var word in page.GetWords())
                {
                    var y = Math.Round(word.BoundingBox.Bottom, 1);
                    // Sang toạ độ dọc khác nghĩa là xuống dòng mới. Nới 2 đơn vị để chữ cùng dòng nhưng lệch
                    // nhau vài phần mười (dấu tiếng Việt kéo chiều cao ô chữ) không bị tách thành hai dòng.
                    if (!double.IsNaN(lastY) && Math.Abs(y - lastY) > 2)
                    {
                        lineList.Add(builder.ToString());
                        builder.Clear();
                    }
                    else if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(word.Text);
                    lastY = y;
                }

                if (builder.Length > 0) lineList.Add(builder.ToString());
            }
        }

        return ReadSeparatedLine(lineList);
    }

    // ==================================================================
    //  DÙNG CHUNG CHO WORD-KHÔNG-BẢNG VÀ PDF
    // ==================================================================

    /// <summary>Mỗi dòng chứa dấu gạch đứng là một bản ghi. Dòng đầu tiên có gạch đứng được coi là tiêu đề
    /// cột và bị bỏ qua — file mẫu luôn in tiêu đề cột theo đúng cách viết đó.</summary>
    private static List<ImportRow> ReadSeparatedLine(IEnumerable<string> lineList)
    {
        var result = new List<ImportRow>();
        var lineNumber = 0;
        var headerSkipped = false;

        foreach (var line in lineList)
        {
            lineNumber++;
            if (line.IndexOf(ColumnSeparator) < 0) continue;

            if (!headerSkipped) { headerSkipped = true; continue; }

            var cellList = line.Split(ColumnSeparator).Select(x => x.Trim()).ToArray();
            var row = new ImportRow(lineNumber, cellList);
            if (!row.IsEmpty) result.Add(row);
        }

        return result;
    }
}
