// File: Application/Common/Interfaces/IExternalCertificateVerifyQueue.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Hàng đợi "giấy chứng nhận bên ngoài này cần AI đối chiếu".
///
/// Phần lớn giấy được đối chiếu NGAY lúc gửi, bằng kết quả AI đã quét khi nhân viên vừa chụp. Giấy vào hàng đợi
/// là những tờ không có kết quả quét sẵn (máy chủ AI tắt lúc chụp, máy chủ khởi động lại giữa chừng): AI đọc chạy
/// nền vì trên máy chạy CPU một lượt đọc mất cả phút. Hàng đợi chỉ để làm ngay — lượt quét định kỳ của dịch vụ nền
/// vẫn vét lại mọi giấy đang chờ, nên mất phần trong bộ nhớ khi tắt máy chủ cũng không sao.
/// </summary>
public interface IExternalCertificateVerifyQueue
{
    /// <summary>Báo giấy cần đối chiếu. Gọi nhiều lần cho cùng một giấy là vô hại — hàng đợi tự gộp.</summary>
    void Enqueue(int certificateId);

    /// <summary>Chờ tới khi có giấy cần đối chiếu, rồi trả về (và xóa khỏi hàng đợi) TẤT CẢ mã đang chờ.</summary>
    Task<IReadOnlyCollection<int>> DequeueAllAsync(CancellationToken cancellationToken);
}
