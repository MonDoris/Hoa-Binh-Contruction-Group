// File: Application/DTOs/HrSyncDto.cs
namespace HbELearning.Application.DTOs;

/// <param name="IsConfigured">false = chưa có API Key / địa chỉ — giao diện hiện hướng dẫn thay vì nút đồng bộ.</param>
/// <param name="LastRun">Lượt đồng bộ THẬT gần nhất kể từ khi máy chủ khởi động (xem trước không tính). Null = chưa có.</param>
public record HrSyncStatusDto(bool IsConfigured, string ProviderName, string DefaultRoleName, HrSyncResultDto? LastRun);

/// <summary>Kết quả (hoặc bản xem trước) của một lượt đồng bộ.</summary>
/// <param name="IsPreview">true = chỉ tính thử, chưa ghi gì vào hệ thống.</param>
/// <param name="IssueCount">Tổng số dòng bị bỏ qua / cần xem lại; IssueList chỉ giữ phần đầu.</param>
public record HrSyncResultDto(
    bool IsPreview, DateTime RunAt,
    int DepartmentCreated, int DepartmentUpdated,
    int PositionCreated, int PositionUpdated,
    int UserCreated, int UserUpdated, int UserDeactivated, int UserUnchanged,
    int IssueCount, IReadOnlyList<HrSyncIssueDto> IssueList);

public record HrSyncIssueDto(string Subject, string Message);
