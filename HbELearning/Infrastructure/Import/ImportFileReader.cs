// File: Infrastructure/Import/ImportFileReader.cs

namespace HbELearning.Infrastructure.Import;

/// <summary>Hiện thực IImportFileReader bằng ImportSourceReader (ClosedXML / OpenXml / PdfPig).</summary>
public sealed class ImportFileReader : IImportFileReader
{
    public List<ImportRow> Read(Stream stream, ImportFormat format) => ImportSourceReader.Read(stream, format);
}
