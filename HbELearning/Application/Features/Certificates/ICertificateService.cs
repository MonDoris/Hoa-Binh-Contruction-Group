// File: Application/Features/Certificates/ICertificateService.cs

namespace HbELearning.Application.Features.Certificates;

/// <summary>Các ca sử dụng của chức năng Certificate. Tầng API gọi qua cổng này (xem API/Controllers/CertificateService.cs).</summary>
public interface ICertificateService
{
    /// <summary>Chứng nhận của tôi.</summary>
    Task<Result> GetMy();

    /// <summary>Toàn bộ chứng nhận đã cấp — Admin/Trainer.</summary>
    Task<Result> GetList(int? courseId, int? departmentId);

    Task<Result> GetById(int id);

    /// <summary>Những người thuộc quyền quản lý của người đang đăng nhập, kèm số chứng nhận của từng người.
    ///
    /// Hệ thống KHÔNG lưu liên kết người-quản-lý-người, nên "cấp dưới" được suy ra từ hai thứ đã có:
    /// bậc của vai trò (xem RoleName.RankOf) và phòng ban. Admin / Tổng Giám Đốc nhìn toàn công ty; Giám Đốc
    /// và Trưởng phòng chỉ thấy người bậc thấp hơn TRONG phòng ban của chính mình — đúng ranh giới mà
    /// RankingService và CourseService đang áp.
    ///
    /// Học viên không quản ai nên nhận về danh sách rỗng, KHÔNG phải 403: giao diện gọi endpoint này cho mọi
    /// vai trò rồi mới quyết định có vẽ khối đó ra hay không, mà một cái 403 ở đó chỉ tổ đỏ console.</summary>
    Task<Result> GetSubordinateList(string? keyword, int? departmentId);

    /// <summary>Các phòng ban có nhân viên thuộc quyền quản lý của người đang đăng nhập, kèm số nhân viên.
    ///
    /// Đây là bậc lọc THỨ NHẤT: Admin và Tổng Giám Đốc quản cả công ty, nên đổ thẳng vài nghìn cái tên ra một
    /// thanh cuộn ngang thì không ai tìm nổi ai. Chọn phòng ban trước rồi mới tới tên là cách thu hẹp mà
    /// người dùng vốn đã nghĩ sẵn trong đầu.
    ///
    /// Trưởng phòng / Giám Đốc chỉ quản đúng phòng mình nên danh sách này trả về một dòng — giao diện thấy
    /// vậy thì bỏ qua luôn bậc lọc thứ nhất, không bắt ai bấm một lựa chọn duy nhất.</summary>
    Task<Result> GetSubordinateDepartmentList();

    /// <summary>Chứng nhận của một người cụ thể — chính mình, hoặc một người thuộc quyền quản lý của mình.</summary>
    Task<Result> GetByUser(int userId);

    /// <summary>Xác thực chứng nhận công khai qua mã QR — KHÔNG cần đăng nhập.</summary>
    Task<Result> Verify(string code);

    /// <summary>Cấp chứng nhận thủ công (trường hợp đặc biệt) — chỉ Quản trị hệ thống.</summary>
    Task<Result> Issue(int enrollmentId);

    Task<Result> Revoke(int id);

    /// <summary>Danh sách chứng nhận để Admin in: chứng nhận nội bộ còn hiệu lực + chứng nhận bên ngoài đã được
    /// xác nhận, gộp chung một khuôn. <paramref name="source"/>: "internal" | "external" | null = cả hai.
    /// Khoảng ngày lọc theo NGÀY CẤP.</summary>
    Task<Result> GetRegister(string? source, int? departmentId, DateTime? from, DateTime? to, string? keyword);
}
