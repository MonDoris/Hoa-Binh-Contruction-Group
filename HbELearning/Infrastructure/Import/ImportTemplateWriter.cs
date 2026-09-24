// File: Infrastructure/Import/ImportTemplateWriter.cs
using ClosedXML.Excel;

namespace HbELearning.Infrastructure.Import;

/// <summary>
/// Hiện thực IImportTemplateWriter: ClosedXML cho Excel, WordTemplateBuilder cho Word.
///
/// Các bước định dạng chép nguyên từ ExcelImportService cũ — năm hàm Build*Template ở đó làm cùng một trình tự
/// (tiêu đề → dòng ví dụ → độ rộng cột → đóng băng dòng 1 → trang hướng dẫn), chỉ khác nội dung. Giờ trình tự đó
/// viết một lần ở đây, nội dung truyền vào qua ImportTemplate.
/// </summary>
public sealed class ImportTemplateWriter : IImportTemplateWriter
{
    public byte[] BuildWord(ImportTemplate template)
        => WordTemplateBuilder.Build(template.Title, template.Header, template.Sample, template.Guide);

    public byte[] BuildWordExam(ExamTemplate template) => WordExamTemplateBuilder.Build(template);

    public byte[] BuildExcel(ImportTemplate template)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(template.SheetName);

        WriteHeader(sheet, template.Header);
        WriteSample(sheet, template.Sample, template.Header.Length);

        for (var i = 0; i < template.ColumnWidthList.Length; i++)
            sheet.Column(i + 1).Width = template.ColumnWidthList[i];
        sheet.SheetView.FreezeRows(1);

        BuildGuideSheet(workbook, template.Guide);

        using var memory = new MemoryStream();
        workbook.SaveAs(memory);
        return memory.ToArray();
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] header)
    {
        for (var i = 0; i < header.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = header[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        sheet.Row(1).Height = 26;
    }

    /// <summary>Ghi các dòng ví dụ, tô nền vàng nhạt để người dùng biết cần xóa.
    /// Ô nào là số nguyên thì ghi xuống dạng số, không thì Excel hiện cảnh báo "số lưu dạng chuỗi" trên
    /// từng ô Điểm/Thời lượng.</summary>
    private static void WriteSample(IXLWorksheet sheet, string[][] sampleList, int columnCount)
    {
        for (var r = 0; r < sampleList.Length; r++)
        {
            var sample = sampleList[r];
            for (var c = 0; c < sample.Length; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                if (int.TryParse(sample[c], out var number)) cell.Value = (double)number;
                else cell.Value = sample[c];
            }

            var range = sheet.Range(r + 2, 1, r + 2, columnCount);
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF3C7");
            range.Style.Alignment.WrapText = true;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }
    }

    private static void BuildGuideSheet(XLWorkbook workbook, (string Field, string Note)[] guideList)
    {
        var sheet = workbook.Worksheets.Add("HuongDan");

        sheet.Cell(1, 1).Value = "HƯỚNG DẪN NHẬP LIỆU";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#2563EB");

        var row = 3;
        foreach (var (field, note) in guideList)
        {
            sheet.Cell(row, 1).Value = field;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = note;
            sheet.Cell(row, 2).Style.Alignment.WrapText = true;
            row++;
        }

        sheet.Column(1).Width = 26;
        sheet.Column(2).Width = 92;
    }
}
