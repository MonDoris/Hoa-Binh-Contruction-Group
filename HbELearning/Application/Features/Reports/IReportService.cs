// File: Application/Features/Reports/IReportService.cs

namespace HbELearning.Application.Features.Reports;

/// <summary>Các ca sử dụng của chức năng Report. Tầng API gọi qua cổng này (xem API/Controllers/ReportService.cs).</summary>
public interface IReportService
{
    /// <summary>Số liệu tổng hợp cho trang chủ công khai — KHÔNG yêu cầu đăng nhập.</summary>
    Task<Result> GetPublicStat();

    /// <summary>Tổng quan toàn hệ thống — dùng cho dashboard.</summary>
    Task<Result> GetOverview();

    /// <summary>Lượt đào tạo hoàn thành / chưa hoàn thành theo tháng của một năm (mặc định năm nay) và theo năm.</summary>
    Task<Result> GetCompletionTrend(int? year);

    /// <summary>Thống kê theo từng khóa học.</summary>
    Task<Result> GetCourseReport();

    /// <summary>Thống kê theo phòng ban. Giám Đốc chỉ xem được phòng ban mình phụ trách; Tổng Giám Đốc xem toàn công ty.</summary>
    Task<Result> GetDepartmentReport();

    /// <summary>Thống kê theo từng người học. Trưởng phòng/Giám Đốc chỉ xem được người học trong phòng ban của mình.</summary>
    Task<Result> GetLearnerReport(int? departmentId);
}
