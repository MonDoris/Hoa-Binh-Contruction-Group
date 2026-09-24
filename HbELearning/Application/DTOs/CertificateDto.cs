// File: Application/DTOs/CertificateDto.cs
namespace HbELearning.Application.DTOs;

public record CertificateDto(
    int Id, int EnrollmentId, string CertificateNo, string VerificationCode,
    DateTime IssuedAt, DateTime? ExpiredAt, decimal FinalScore, string? QrCodeUrl, bool IsRevoked,
    int CourseId, string CourseCode, string CourseTitle,
    int UserId, string UserFullName, string EmployeeCode, string? DepartmentName, string? PositionName);

/// <summary>Một nhân viên thuộc quyền quản lý của người đang đăng nhập, kèm số chứng nhận đã được cấp —
/// dùng cho khối "Chứng nhận của nhân viên cấp dưới" ở trang Chứng nhận.
///
/// CertificateCount đếm CẢ chứng nhận đã thu hồi, để con số ở đầu dòng luôn khớp đúng số thẻ xổ ra bên dưới
/// khi bấm vào tên. Đếm riêng phần còn hiệu lực thì người xem sẽ tưởng giao diện đang giấu bớt gì đó.</summary>
/// <summary>Một phòng ban có nhân viên thuộc quyền quản lý của người đang đăng nhập — dùng cho bậc lọc
/// THỨ NHẤT ở trang Chứng nhận (phòng ban -> tên nhân viên).</summary>
public record CertificateDepartmentDto(int DepartmentId, string Name, int EmployeeCount);

public record CertificateSubordinateDto(
    int UserId, string FullName, string EmployeeCode, string? AvatarUrl,
    string? DepartmentName, string? PositionName, string RoleName, int CertificateCount);

/// <summary>Kết quả xác thực công khai qua QR Code — không yêu cầu đăng nhập.</summary>
public record CertificateVerifyDto(
    bool IsValid, string Message,
    string? CertificateNo, string? UserFullName, string? EmployeeCode,
    string? CourseTitle, DateTime? IssuedAt, DateTime? ExpiredAt, decimal? FinalScore);
