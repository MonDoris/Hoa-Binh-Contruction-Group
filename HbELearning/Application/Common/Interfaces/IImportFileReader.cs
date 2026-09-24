// File: Application/Common/Interfaces/IImportFileReader.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Bóc tách tệp người dùng tải lên (Excel / Word / PDF) thành danh sách dòng đã tách ô.
///
/// Chỉ làm đúng việc bóc tách — dòng 1 là tiêu đề cột nên bị bỏ, dòng trống bị bỏ, số dòng giữ đúng số dòng thật
/// trong tệp để báo lỗi. Mọi kiểm tra hợp lệ và ghi database nằm ở nghiệp vụ nhập liệu (DataImportService).
/// </summary>
public interface IImportFileReader
{
    List<ImportRow> Read(Stream stream, ImportFormat format);
}
