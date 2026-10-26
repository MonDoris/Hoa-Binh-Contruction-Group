using HbELearning.Application.Common.Interfaces;
using HbELearning.Application.Common.Models;
using HbELearning.Application.Common.Results;
using HbELearning.Application.DTOs;
using HbELearning.Application.Features.HrSync;
using HbELearning.Domain.Constants;
using HbELearning.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HbELearning.Tests.HrSync;

/// <summary>
/// Đồng bộ nhân sự ghi hàng loạt vào tài khoản thật nên mỗi quy tắc an toàn đều có một ca kiểm: không đụng Admin,
/// không khóa người chỉ vì vắng mặt trong danh sách, không ghi đè người khác chỉ vì trùng mã, xem trước không ghi gì.
/// </summary>
public class HrSyncServiceTests
{
    private sealed class FakeHrClient : IHrSystemClient
    {
        public bool IsConfigured { get; set; } = true;
        public string ProviderName => "Fake";
        public HrSnapshot Snapshot { get; set; } = new([], [], []);
        public Exception? Failure { get; set; }

        public Task<HrSnapshot> FetchAsync(CancellationToken ct = default)
            => Failure is null ? Task.FromResult(Snapshot) : Task.FromException<HrSnapshot>(Failure);
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string secret) => "hash:" + secret.Length;
        public bool Verify(string secret, string hash) => false;
    }

    private static (HrSyncService Service, FakeHrClient Client, ClassRoomWorld World) Build()
    {
        var world = ClassRoomWorld.Create();
        var client = new FakeHrClient();
        var service = new HrSyncService(world.NewDb(), client, new FakeHasher(), new HrSyncLastRunStore(),
            new ConfigurationBuilder().Build(), NullLogger<HrSyncService>.Instance);
        return (service, client, world);
    }

    private static HrSyncResultDto Ok(Result result)
    {
        Assert.Equal(ResultKind.Success, result.Kind);
        return Assert.IsType<HrSyncResultDto>(result.Value);
    }

    private static HrSnapshot Snapshot(params HrEmployeeRecord[] employees) => new(
        [new HrDepartmentRecord("IT", "Phòng IT")],
        [new HrPositionRecord("DEV", "Lập trình viên", "IT")],
        employees);

    [Fact]
    public async Task Chua_co_API_Key_thi_bao_chua_ket_noi_va_khong_goi_di_dau()
    {
        var (service, client, _) = Build();
        client.IsConfigured = false;

        var result = await service.Run(previewOnly: false);

        Assert.Equal(ResultKind.ServiceUnavailable, result.Kind);
    }

    [Fact]
    public async Task Phan_mem_nhan_su_loi_thi_bao_loi_va_khong_doi_gi()
    {
        var (service, client, world) = Build();
        client.Failure = new HrSystemException("API Key bị từ chối");
        var before = world.NewDb().User.Count();

        var result = await service.Run(previewOnly: false);

        Assert.Equal(ResultKind.ExternalServiceFailed, result.Kind);
        Assert.Equal(before, world.NewDb().User.Count());
    }

    [Fact]
    public async Task Xem_truoc_dem_dung_nhung_khong_ghi_gi()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("E100", "Nguyễn A", "a@corp.vn", null, "IT", "DEV"));
        var before = world.NewDb().User.Count();

        var preview = Ok(await service.Run(previewOnly: true));

        Assert.True(preview.IsPreview);
        Assert.Equal(1, preview.UserCreated);
        Assert.Equal(1, preview.DepartmentCreated);
        Assert.Equal(before, world.NewDb().User.Count());
        Assert.False(world.NewDb().Department.Any(x => x.Code == "IT"));
    }

    [Fact]
    public async Task Nguoi_moi_duoc_tao_voi_vai_tro_hoc_vien_dung_phong_ban_va_vi_tri()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("E100", "Nguyễn A", "A@Corp.vn", "0911", "IT", "DEV"));

        var result = Ok(await service.Run(previewOnly: false));

        Assert.Equal(1, result.UserCreated);
        using var db = world.NewDb();
        var user = db.User.Include(x => x.Role).Include(x => x.Department).Include(x => x.Position).Single(x => x.EmployeeCode == "E100");
        Assert.Equal(RoleName.Learner, user.Role.Name);
        Assert.Equal("a@corp.vn", user.Email);
        Assert.Equal("IT", user.Department!.Code);
        Assert.Equal("DEV", user.Position!.Code);
        Assert.Equal(user.DepartmentId, user.Position.DepartmentId);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task Chay_hai_lan_thi_lan_hai_khong_thay_doi_gi()
    {
        var (service, client, _) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("E100", "Nguyễn A", "a@corp.vn", null, "IT", "DEV"));

        Ok(await service.Run(previewOnly: false));
        var second = Ok(await service.Run(previewOnly: false));

        Assert.Equal(0, second.UserCreated);
        Assert.Equal(0, second.UserUpdated);
        Assert.Equal(0, second.DepartmentCreated);
        Assert.Equal(0, second.PositionCreated);
    }

    [Fact]
    public async Task Nguoi_da_co_duoc_cap_nhat_ten_va_phong_ban_nhung_giu_nguyen_vai_tro()
    {
        var (service, client, world) = Build();
        // Student (mã STU) đã có sẵn; cùng tên nên được nhận ra là cùng một người.
        client.Snapshot = Snapshot(new HrEmployeeRecord("STU", "Học Viên", null, "0922", "IT", "DEV"));
        var roleBefore = world.NewDb().User.Single(x => x.EmployeeCode == "STU").RoleId;

        var result = Ok(await service.Run(previewOnly: false));

        Assert.Equal(1, result.UserUpdated);
        using var db = world.NewDb();
        var user = db.User.Include(x => x.Department).Single(x => x.EmployeeCode == "STU");
        Assert.Equal("IT", user.Department!.Code);
        Assert.Equal("0922", user.PhoneNumber);
        Assert.Equal(roleBefore, user.RoleId);
    }

    [Fact]
    public async Task Chi_khoa_nguoi_ma_phan_mem_nhan_su_bao_da_nghi_viec()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("STU", "Học Viên", null, null, "IT", "DEV", IsActive: false));

        var result = Ok(await service.Run(previewOnly: false));

        Assert.Equal(1, result.UserDeactivated);
        Assert.False(world.NewDb().User.Single(x => x.EmployeeCode == "STU").IsActive);
    }

    [Fact]
    public async Task Vang_mat_trong_danh_sach_khong_bi_khoa()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("E100", "Nguyễn A", "a@corp.vn", null, "IT", "DEV"));

        Ok(await service.Run(previewOnly: false));

        // HOST / STU / OUT không có trong danh sách nhân sự nhưng phải còn hoạt động.
        Assert.All(world.NewDb().User.Where(x => x.EmployeeCode == "HOST" || x.EmployeeCode == "STU" || x.EmployeeCode == "OUT"),
            u => Assert.True(u.IsActive));
    }

    [Fact]
    public async Task Tai_khoan_Admin_khong_bi_sua_hay_khoa()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("ADM", "Tên Khác Hẳn", "khac@corp.vn", null, "IT", "DEV", IsActive: false));

        var result = Ok(await service.Run(previewOnly: false));

        Assert.Equal(0, result.UserUpdated);
        Assert.Equal(0, result.UserDeactivated);
        var admin = world.NewDb().User.Single(x => x.EmployeeCode == "ADM");
        Assert.True(admin.IsActive);
        Assert.Equal("Quản Trị", admin.FullName);
    }

    [Fact]
    public async Task Trung_ma_nhung_khac_ca_ten_lan_email_thi_bo_qua_va_bao()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("STU", "Người Hoàn Toàn Khác", "khac@corp.vn", null, "IT", "DEV"));

        var result = Ok(await service.Run(previewOnly: false));

        Assert.Equal(0, result.UserUpdated);
        Assert.Equal(1, result.IssueCount);
        Assert.Equal("Học Viên", world.NewDb().User.Single(x => x.EmployeeCode == "STU").FullName);
    }

    [Fact]
    public async Task Thieu_email_hoac_phong_ban_la_thi_bao_loi_dung_cho()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(
            new HrEmployeeRecord("E200", "Không Email", null, null, "IT", "DEV"),
            new HrEmployeeRecord("E201", "Phòng Lạ", "la@corp.vn", null, "KHONG-CO", "DEV"));

        var result = Ok(await service.Run(previewOnly: false));

        Assert.Equal(1, result.UserCreated);                                  // chỉ E201 được tạo
        Assert.False(world.NewDb().User.Any(x => x.EmployeeCode == "E200"));
        Assert.True(world.NewDb().User.Any(x => x.EmployeeCode == "E201"));
        Assert.Equal(2, result.IssueCount);                                   // thiếu email + phòng ban lạ
    }

    [Fact]
    public async Task Nguoi_da_nghi_tu_truoc_thi_khong_tao_tai_khoan()
    {
        var (service, client, world) = Build();
        client.Snapshot = Snapshot(new HrEmployeeRecord("E300", "Đã Nghỉ", "nghi@corp.vn", null, "IT", "DEV", IsActive: false));

        var result = Ok(await service.Run(previewOnly: false));

        Assert.Equal(0, result.UserCreated);
        Assert.False(world.NewDb().User.Any(x => x.EmployeeCode == "E300"));
    }
}
