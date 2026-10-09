// File: Application/Features/ExternalCertificates/IExternalCertificateService.cs

namespace HbELearning.Application.Features.ExternalCertificates;

/// <summary>Chứng nhận nhân viên lấy được khi học bên ngoài: nhân viên tự gửi (chụp camera / tải PDF), AI đọc giấy
/// và đối chiếu rồi tự xác nhận hoặc từ chối; Admin chỉ quản lý (xem, xóa, sửa quyết định khi AI đọc sai). Xem
/// <see cref="ExternalCertificate"/>.</summary>
public interface IExternalCertificateService
{
    /// <summary>Giấy của chính người đang đăng nhập, mới gửi trước.</summary>
    Task<Result> GetMy();

    /// <summary>Danh sách cho Admin tra cứu. <paramref name="status"/> null = mọi trạng thái.</summary>
    Task<Result> GetList(ExternalCertificateStatus? status, int? departmentId, string? keyword);

    /// <summary>Số giấy AI chưa đối chiếu xong — con số trên thẻ chứng nhận bên ngoài của Admin.</summary>
    Task<Result> GetPendingCount();

    /// <summary>AI đọc giấy vừa chụp / chọn (chưa lưu gì): trả về những gì đọc được để điền sẵn biểu mẫu, và
    /// báo sớm nếu giấy chắc chắn không qua được. Kết quả được giữ lại để lúc Gửi đối chiếu ngay, khỏi đọc lại.</summary>
    Task<Result> Scan(FileUpload? file, CancellationToken cancellationToken);

    /// <summary>Gửi giấy. Đã có kết quả quét của đúng tệp này thì đối chiếu ngay: khớp là xác nhận luôn, lệch thì
    /// trả 400 kèm chỗ lệch và KHÔNG lưu. Chưa có thì lưu ở trạng thái chờ, AI đối chiếu nền.</summary>
    Task<Result> Create(ExternalCertificateRequest request, FileUpload? file);

    /// <summary>Nhân viên xóa giấy chưa được xác nhận của mình (gửi nhầm, hoặc bị từ chối cần gửi lại); Admin
    /// xóa được mọi giấy.</summary>
    Task<Result> Delete(int id);

    /// <summary>Nội dung tệp quét — chỉ chủ giấy và Admin đọc được.</summary>
    Task<Result> GetFile(int id);

    /// <summary>Admin xác nhận tay (giấy đang chờ, hoặc AI từ chối nhầm).</summary>
    Task<Result> Approve(int id);

    /// <summary>Admin từ chối tay, hoặc thu hồi một giấy đã được xác nhận.</summary>
    Task<Result> Reject(int id, ExternalCertificateRejectRequest request);
}
