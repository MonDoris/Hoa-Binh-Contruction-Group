using HbELearning.Application.DTOs;
using HbELearning.Application.Features.ExternalCertificates;
using HbELearning.Domain.Constants;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.Certificates;

/// <summary>
/// Chứng nhận bên ngoài: sau khi AI (hoặc Admin) xác nhận, một người của phòng Nhân sự bấm xác nhận — giấy chuyển sang
/// "Phòng nhân sự đã xác nhận" và lưu tên người bấm. Chỉ người phòng Nhân sự, không tự xác nhận giấy của mình.
/// </summary>
public class ExternalHrConfirmTests
{
    private sealed record Org(ClassRoomWorld World, User HrStaff, User Employee, int CertificateId);

    private static Org Seed(ExternalCertificateStatus status = ExternalCertificateStatus.Approved)
    {
        var world = ClassRoomWorld.Create();
        using var db = world.NewDb();
        var learnerRoleId = db.Role.Single(r => r.Name == RoleName.Learner).Id;

        var hr = new Department { Code = "NS", Name = "Phòng Nhân sự", IsHrDepartment = true };
        var it = new Department { Code = "IT", Name = "Phòng IT" };
        db.Department.AddRange(hr, it);
        db.SaveChanges();

        User Person(string code, int deptId) => new()
        {
            EmployeeCode = code, FullName = "Người " + code, Email = $"{code.ToLowerInvariant()}@test.local",
            PasswordHash = "x", RoleId = learnerRoleId, DepartmentId = deptId,
        };
        var hrStaff = Person("HR1", hr.Id);
        var employee = Person("EMP", it.Id);
        db.User.AddRange(hrStaff, employee);
        db.SaveChanges();

        var certificate = new ExternalCertificate
        {
            UserId = employee.Id, Title = "Tiếng Anh Cấp Tốc", Issuer = "DOL English", IssuedAt = new DateTime(2026, 10, 1),
            FilePath = "external-certificate/x.pdf", FileName = "x.pdf", ContentType = "application/pdf", FileSize = 10,
        };
        if (status == ExternalCertificateStatus.Approved) certificate.ApproveByAi(DateTime.Now);
        db.ExternalCertificate.Add(certificate);
        db.SaveChanges();
        return new Org(world, hrStaff, employee, certificate.Id);
    }

    private static IExternalCertificateService Service(ClassRoomWorld world) => world.Service<IExternalCertificateService>();

    private static async Task<ExternalCertificateDto> Load(Org org)
    {
        org.World.ActAs(org.Employee);
        var result = await Service(org.World).GetMy();
        return Assert.IsAssignableFrom<IEnumerable<ExternalCertificateDto>>(result.Value).Single();
    }

    [Fact]
    public async Task Hr_staff_confirms_an_ai_approved_certificate_and_their_name_is_kept()
    {
        var org = Seed();
        org.World.ActAs(org.HrStaff);

        var result = await Service(org.World).ConfirmByHr(org.CertificateId);

        Assert.True(result.IsSuccess);
        var dto = await Load(org);
        Assert.Equal("Approved", dto.Status);
        Assert.True(dto.IsAiReviewed);
        Assert.Equal("Người HR1", dto.HrConfirmedByName);
        Assert.NotNull(dto.HrConfirmedAt);
    }

    [Fact]
    public async Task Someone_outside_hr_cannot_confirm()
    {
        var org = Seed();
        var outsider = org.World.AddPerson("OUT2", "Người ngoài");
        org.World.ActAs(outsider);

        var result = await Service(org.World).ConfirmByHr(org.CertificateId);

        Assert.False(result.IsSuccess);
        Assert.Null((await Load(org)).HrConfirmedAt);
    }

    [Fact]
    public async Task A_certificate_still_waiting_for_ai_cannot_be_confirmed()
    {
        var org = Seed(ExternalCertificateStatus.Pending);
        org.World.ActAs(org.HrStaff);

        Assert.False((await Service(org.World).ConfirmByHr(org.CertificateId)).IsSuccess);
    }

    [Fact]
    public async Task Confirming_twice_is_refused()
    {
        var org = Seed();
        org.World.ActAs(org.HrStaff);
        await Service(org.World).ConfirmByHr(org.CertificateId);

        Assert.False((await Service(org.World).ConfirmByHr(org.CertificateId)).IsSuccess);
    }

    [Fact]
    public async Task Hr_staff_can_list_every_certificate_but_an_ordinary_employee_cannot()
    {
        var org = Seed();

        org.World.ActAs(org.HrStaff);
        Assert.True((await Service(org.World).GetList(null, null, null)).IsSuccess);

        org.World.ActAs(org.Employee);
        Assert.False((await Service(org.World).GetList(null, null, null)).IsSuccess);
    }

    [Fact]
    public async Task Admin_revoking_clears_the_hr_confirmation()
    {
        var org = Seed();
        org.World.ActAs(org.HrStaff);
        await Service(org.World).ConfirmByHr(org.CertificateId);

        org.World.ActAs(org.World.Admin);
        await Service(org.World).Reject(org.CertificateId, new ExternalCertificateRejectRequest { Reason = "Giấy giả" });

        var dto = await Load(org);
        Assert.Equal("Rejected", dto.Status);
        Assert.Null(dto.HrConfirmedByName);
        Assert.Null(dto.HrConfirmedAt);
    }
}
