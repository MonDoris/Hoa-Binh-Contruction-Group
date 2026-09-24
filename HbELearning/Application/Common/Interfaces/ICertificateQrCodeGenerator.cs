// File: Application/Common/Interfaces/ICertificateQrCodeGenerator.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Tạo ảnh mã QR để quét xác thực một chứng nhận.
///
/// Tách khỏi nghiệp vụ cấp chứng nhận: vẽ QR (thư viện QRCoder), biết địa chỉ công khai của máy chủ (đọc từ yêu
/// cầu HTTP) và ghi tệp PNG ra đĩa đều là chi tiết hạ tầng. Nghiệp vụ chỉ cần biết "ảnh QR nằm ở đường dẫn nào".
/// </summary>
public interface ICertificateQrCodeGenerator
{
    /// <summary>Tạo và lưu ảnh QR trỏ tới trang xác thực công khai của mã này; trả đường dẫn tương đối của ảnh
    /// (VD "/uploads/qr/abc.png").</summary>
    string Generate(string verificationCode);
}
