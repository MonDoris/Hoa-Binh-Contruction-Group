// File: Application/Common/Models/HrModels.cs
namespace HbELearning.Application.Common.Models;

// Dữ liệu nhân sự đọc từ phần mềm quản lý nhân sự bên ngoài, đã đổi về hình dạng chung của hệ thống.
// Khóa nối giữa hai bên là MÃ: mã phòng ban = Department.Code, mã vị trí = Position.Code, mã nhân viên =
// User.EmployeeCode — nhờ vậy không cần thêm cột "mã ngoài" nào vào cơ sở dữ liệu.

public record HrDepartmentRecord(string Code, string Name);

public record HrPositionRecord(string Code, string Name, string? DepartmentCode);

/// <param name="IsActive">false = phần mềm nhân sự báo đã nghỉ việc — hệ thống sẽ vô hiệu hóa tài khoản.</param>
public record HrEmployeeRecord(
    string EmployeeCode, string FullName, string? Email, string? PhoneNumber,
    string? DepartmentCode, string? PositionCode, bool IsActive = true);

public record HrSnapshot(
    IReadOnlyList<HrDepartmentRecord> DepartmentList,
    IReadOnlyList<HrPositionRecord> PositionList,
    IReadOnlyList<HrEmployeeRecord> EmployeeList);
