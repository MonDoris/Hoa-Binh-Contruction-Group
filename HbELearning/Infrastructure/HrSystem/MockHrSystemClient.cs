// File: Infrastructure/HrSystem/MockHrSystemClient.cs
namespace HbELearning.Infrastructure.HrSystem;

/// <summary>Phần mềm nhân sự GIẢ để chạy thử toàn bộ luồng đồng bộ (xem trước, đồng bộ, báo lỗi) khi chưa có API
/// Key thật. Chỉ bật khi HrSystem:Provider = "Mock". Mọi mã đều có tiền tố "MOCK" để dễ nhận ra và dọn sau —
/// nhớ rằng bấm "Đồng bộ" (không phải "Xem trước") với Provider này sẽ GHI các tài khoản giả vào cơ sở dữ liệu đang chạy.</summary>
public class MockHrSystemClient : IHrSystemClient
{
    public string ProviderName => "Dữ liệu giả (Mock)";
    public bool IsConfigured => true;

    public Task<HrSnapshot> FetchAsync(CancellationToken ct = default)
    {
        var departmentList = new List<HrDepartmentRecord>
        {
            new("MOCK-IT", "[Mock] Phòng Công nghệ thông tin"),
            new("MOCK-KT", "[Mock] Phòng Kế toán"),
            new("MOCK-SX", "[Mock] Phòng Sản xuất"),
        };
        var positionList = new List<HrPositionRecord>
        {
            new("MOCK-DEV", "[Mock] Lập trình viên", "MOCK-IT"),
            new("MOCK-KTV", "[Mock] Kế toán viên", "MOCK-KT"),
            new("MOCK-CN", "[Mock] Công nhân", "MOCK-SX"),
        };
        var employeeList = new List<HrEmployeeRecord>
        {
            new("MOCK001", "Nguyễn Văn An", "mock001@example.local", "0900000001", "MOCK-IT", "MOCK-DEV"),
            new("MOCK002", "Trần Thị Bình", "mock002@example.local", "0900000002", "MOCK-KT", "MOCK-KTV"),
            new("MOCK003", "Lê Văn Cường", "mock003@example.local", "0900000003", "MOCK-SX", "MOCK-CN"),
            new("MOCK004", "Phạm Thị Dung", "mock004@example.local", null, "MOCK-SX", "MOCK-CN", IsActive: false),
            new("MOCK005", "Hoàng Văn Em", null, null, "MOCK-IT", "MOCK-DEV"),            // thiếu email → báo lỗi, không tạo
            new("MOCK006", "Võ Thị Phương", "mock006@example.local", null, "KHONG-CO", "MOCK-KTV"), // phòng ban lạ → báo cảnh báo
        };
        return Task.FromResult(new HrSnapshot(departmentList, positionList, employeeList));
    }
}
