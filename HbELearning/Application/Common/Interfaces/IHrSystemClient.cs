// File: Application/Common/Interfaces/IHrSystemClient.cs
namespace HbELearning.Application.Common.Interfaces;

/// <summary>Cổng đọc dữ liệu từ phần mềm quản lý nhân sự bên ngoài. Application chỉ biết cổng này; còn gọi API
/// nào, xác thực ra sao do lớp hiện thực bên Infrastructure lo — đổi sang phần mềm nhân sự khác chỉ phải thay
/// lớp hiện thực, không đụng tới nghiệp vụ đồng bộ.</summary>
public interface IHrSystemClient
{
    /// <summary>Đã có đủ thông tin kết nối (địa chỉ + API Key) chưa. false thì không được gọi <see cref="FetchAsync"/>.</summary>
    bool IsConfigured { get; }

    /// <summary>Tên hiển thị của phần mềm nhân sự, để giao diện nói rõ đang đồng bộ từ đâu.</summary>
    string ProviderName { get; }

    /// <summary>Đọc toàn bộ phòng ban, vị trí và nhân viên. Lỗi kết nối / dữ liệu sai dạng → <see cref="HrSystemException"/>.</summary>
    Task<HrSnapshot> FetchAsync(CancellationToken ct = default);
}

/// <summary>Lỗi khi đọc phần mềm nhân sự. Thông điệp được viết sẵn để hiện cho Admin: không chứa API Key hay
/// địa chỉ nội bộ — chi tiết kỹ thuật đã nằm trong nhật ký.</summary>
public class HrSystemException : Exception
{
    public HrSystemException(string message, Exception? inner = null) : base(message, inner) { }
}
