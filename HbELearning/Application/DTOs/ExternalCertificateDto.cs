// File: Application/DTOs/ExternalCertificateDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Một chứng nhận bên ngoài — dùng cho cả danh sách "của tôi" lẫn màn hình quản lý của Admin.</summary>
/// <param name="Status">"Pending" (AI chưa đối chiếu xong) | "Approved" | "Rejected".</param>
/// <param name="IsAiReviewed">Quyết định hiện tại là của AI; false = Admin chốt tay (hoặc chưa ai chốt).</param>
/// <param name="AiResult">Những gì AI đọc được và kết quả so từng mục — null khi AI chưa đọc.</param>
public record ExternalCertificateDto(
    int Id, int UserId, string UserFullName, string EmployeeCode, string? DepartmentName, string? PositionName,
    string Title, string Issuer, string? CertificateNo, DateTime IssuedAt, DateTime? ExpiredAt, string? Note,
    string FileName, string ContentType, long FileSize,
    string Status, string? RejectReason, string? ReviewedByName, DateTime? ReviewedAt, DateTime CreatedAt,
    bool IsAiReviewed, ExternalCertificateAiResultDto? AiResult);

/// <summary>Kết quả một lần AI đọc giấy — đây cũng là hình dạng lưu ở cột AI_RESULT_JSON.</summary>
/// <param name="CheckList">Kết quả so từng mục (xem CertificateCheck); rỗng khi AI chỉ quét chứ chưa đối chiếu.</param>
public record ExternalCertificateAiResultDto(
    string Model, DateTime ReadAt, bool IsCertificate, bool IsReadable, string? DocumentType,
    string? HolderName, string? Title, string? Issuer, string? CertificateNo, DateTime? IssuedAt, DateTime? ExpiredAt,
    string? Note, List<ExternalCertificateCheckDto> CheckList);

/// <param name="Field">"document" | "name" | "title" | "issuer" | "no" | "issued" | "expired".</param>
/// <param name="Ok">true khớp, false lệch, null không so được.</param>
public record ExternalCertificateCheckDto(string Field, bool? Ok, string? Found, string? Message);

/// <summary>Kết quả AI quét giấy ngay khi nhân viên vừa chụp / chọn tệp — dùng để điền sẵn biểu mẫu và báo sớm
/// những chỗ chắc chắn không qua được (sai người, không phải giấy chứng nhận).</summary>
/// <param name="AccountName">Họ tên tài khoản đang đăng nhập — thứ họ tên trên giấy phải khớp.</param>
/// <param name="IsNameMatched">null khi không đọc được họ tên trên giấy.</param>
/// <param name="Problem">Câu báo chặn (giấy này chắc chắn không được xác nhận), null nếu không có.</param>
public record ExternalCertificateScanDto(
    ExternalCertificateAiResultDto Reading, string AccountName, bool? IsNameMatched, string? Problem);

/// <summary>Thân lỗi 400 khi bấm Gửi mà AI đã đọc giấy và thấy lệch: giấy KHÔNG được lưu, biểu mẫu vẫn mở để nhân
/// viên sửa chỗ khai sai (hoặc chụp lại) — <paramref name="AiResult"/> chỉ ra đúng mục nào lệch.</summary>
public record ExternalCertificateMismatchResponse(string Message, ExternalCertificateAiResultDto AiResult);

/// <summary>Thông tin nhân viên khai khi gửi giấy (tệp đi riêng trong cùng form multipart).
///
/// Mọi trường để nullable và KHÔNG gắn [Required]: ExternalCertificateService tự kiểm và báo lỗi bằng một câu
/// tiếng Việt rõ ràng. Để mô hình tự chặn thì giao diện chỉ nhận được "One or more validation errors occurred."</summary>
public class ExternalCertificateRequest
{
    public string? Title { get; set; }
    public string? Issuer { get; set; }
    public string? CertificateNo { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public string? Note { get; set; }
}

public class ExternalCertificateRejectRequest
{
    public string? Reason { get; set; }
}

/// <summary>Một dòng của danh sách chứng nhận Admin in ra — gộp chứng nhận NỘI BỘ (hệ thống cấp khi học xong
/// khóa) và chứng nhận BÊN NGOÀI đã được xác nhận vào chung một khuôn.</summary>
/// <param name="Source">"Internal" | "External".</param>
/// <param name="Issuer">Đơn vị cấp: với chứng nhận nội bộ là tên hệ thống.</param>
public record CertificateRegisterRowDto(
    string Source, int UserId, string EmployeeCode, string FullName, string? DepartmentName, string? PositionName,
    string Title, string Issuer, string? CertificateNo, DateTime IssuedAt, DateTime? ExpiredAt);
