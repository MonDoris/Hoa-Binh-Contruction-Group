// File: Application/Features/Users/IUserService.cs

namespace HbELearning.Application.Features.Users;

/// <summary>Các ca sử dụng của chức năng User. Tầng API gọi qua cổng này (xem API/Controllers/UserService.cs).</summary>
public interface IUserService
{
    /// <summary>Danh sách người dùng, có phân trang và lọc.</summary>
    Task<Result> GetList(string? keyword, int? departmentId, int? positionId, int? roleId, bool? isActive, int page = 1, int pageSize = 20);

    Task<Result> GetById(int id);

    Task<Result> Create(UserCreateRequest request);

    Task<Result> Update(int id, UserUpdateRequest request);

    /// <summary>Vô hiệu hóa tài khoản (soft delete) — giữ lại lịch sử học tập.</summary>
    Task<Result> Deactivate(int id);

    /// <summary>Xóa vĩnh viễn tài khoản — chỉ cho phép khi tài khoản chưa có dữ liệu hoạt động nào gắn theo
    /// (chưa tạo khóa học, chưa được phân công học, chưa duyệt nội dung, chưa chủ trì phòng học online...).
    /// Nếu đã có dữ liệu, dùng "Khóa" (vô hiệu hóa) thay vì xóa để giữ lại lịch sử.</summary>
    Task<Result> Delete(int id);

    Task<Result> ResetPassword(int id, AdminResetPasswordRequest? request);

    /// <summary>Mở khóa một tài khoản bị khóa vì gõ sai mật khẩu quá số lần cho phép (xem AuthService.Login).
    /// Chỉ xóa cờ khóa và bộ đếm, KHÔNG đụng tới mật khẩu — người dùng vẫn đăng nhập bằng mật khẩu cũ của họ.
    /// Muốn vừa mở khóa vừa cấp mật khẩu mới thì dùng <see cref="ResetPassword"/>, nó mở khóa luôn.</summary>
    Task<Result> Unlock(int id);

    /// <summary>Tải file Excel mẫu để nhập người dùng hàng loạt.</summary>
    Result DownloadImportTemplate();

    /// <summary>Nhập người dùng hàng loạt từ file Excel. Dòng nào sai thì bỏ qua dòng đó và báo lỗi kèm số dòng,
    /// các dòng còn lại vẫn được nhập.</summary>
    Task<Result> Import(FileUpload file);
}
