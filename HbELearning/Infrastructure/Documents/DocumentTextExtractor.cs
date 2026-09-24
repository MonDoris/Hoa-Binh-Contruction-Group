// File: Infrastructure/Documents/DocumentTextExtractor.cs
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace HbELearning.Infrastructure.Documents;

/// <summary>Hiện thực IDocumentTextExtractor bằng PdfPig và OpenXml. Chép nguyên phần "bóc tách văn bản" từng nằm
/// trong DocumentProcessingService.</summary>
public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    public async Task<string> ExtractTextAsync(string filePath, string extension, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Không tìm thấy tài liệu: {filePath}", filePath);

        return extension switch
        {
            "pdf" => ExtractTextFromPdf(filePath),
            "docx" => ExtractTextFromWord(filePath),
            "txt" or "md" => await File.ReadAllTextAsync(filePath, cancellationToken),
            // .doc (Word 97-2003) là định dạng nhị phân cũ, OpenXml KHÔNG đọc được — phải lưu lại thành .docx.
            _ => throw new NotSupportedException($"Chưa hỗ trợ đọc định dạng {extension}. Vui lòng dùng PDF, DOCX hoặc TXT.")
        };
    }

    /// <summary>Đọc chữ từ PDF. Ghép theo "từ" (GetWords) thay vì Page.Text vì Page.Text nối thẳng các ký tự
    /// theo thứ tự vẽ nên thường mất dấu cách giữa các từ.</summary>
    private static string ExtractTextFromPdf(string filePath)
    {
        using var document = PdfDocument.Open(filePath);

        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var pageText = string.Join(' ', page.GetWords().Select(w => w.Text));
            if (!string.IsNullOrWhiteSpace(pageText))
                builder.AppendLine(pageText).AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>Đọc chữ từ file Word .docx, lấy cả nội dung trong bảng (đoạn văn nằm trong ô bảng cũng được duyệt).</summary>
    private static string ExtractTextFromWord(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var builder = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
                builder.AppendLine(text.Trim());
        }

        return builder.ToString();
    }
}
