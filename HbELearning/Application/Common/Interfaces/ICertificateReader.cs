// File: Application/Common/Interfaces/ICertificateReader.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Đọc một giấy chứng nhận (ảnh chụp / tệp PDF) bằng model đọc-được-ảnh CHẠY NỘI BỘ.
///
/// Chỉ ĐỌC — trả về những gì in trên giấy. Việc so với thông tin nhân viên khai và quyết định xác nhận hay từ
/// chối là nghiệp vụ, nằm ở <c>ExternalCertificateMatcher</c>: model nhỏ chạy CPU đọc chữ khá, nhưng để nó tự
/// phán "khớp hay không" thì mỗi lần một kiểu và không ai giải thích được vì sao.
/// </summary>
public interface ICertificateReader
{
    /// <summary>Tên model đang dùng (VD "qwen3-vl:4b-instruct") — ghi kèm kết quả để biết ai đã đọc.</summary>
    string Model { get; }

    /// <summary>Đọc giấy. <paramref name="contentType"/> là kiểu tệp do máy chủ xác định từ nội dung
    /// ("application/pdf", "image/jpeg", "image/png", "image/webp").</summary>
    /// <exception cref="AiUnavailableException">Không gọi được máy chủ AI hoặc chưa cài model — thử lại sau,
    /// không phải lỗi của tờ giấy.</exception>
    /// <exception cref="CertificateReadException">AI có chạy nhưng không ra kết quả dùng được (trả về rác, quá
    /// hạn chờ).</exception>
    Task<CertificateReading> ReadAsync(byte[] content, string contentType, CancellationToken cancellationToken = default);
}

/// <summary>Những gì AI đọc được trên giấy. Trường không có trên giấy hoặc không đọc rõ thì null.</summary>
/// <param name="IsCertificate">Có phải giấy chứng nhận / chứng chỉ / bằng đào tạo không.</param>
/// <param name="IsReadable">Ảnh đủ rõ để đọc họ tên và tên chứng nhận.</param>
/// <param name="Note">Một câu AI tự ghi khi có vấn đề (ảnh lóa, bị cắt...).</param>
public sealed record CertificateReading(
    bool IsCertificate, bool IsReadable, string? DocumentType,
    string? HolderName, string? Title, string? Issuer, string? CertificateNo,
    DateTime? IssuedAt, DateTime? ExpiredAt, string? Note);

/// <summary>Máy chủ AI nội bộ không dùng được lúc này (tắt, chưa cài model). Nơi gọi để việc lại cho lần sau.</summary>
public sealed class AiUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>AI có chạy nhưng lượt đọc này hỏng. Nơi gọi đếm số lần hỏng rồi thôi thử sau vài lần.</summary>
public sealed class CertificateReadException(string message, Exception? innerException = null)
    : Exception(message, innerException);
