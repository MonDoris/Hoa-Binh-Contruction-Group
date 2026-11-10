using HbELearning.Application.DTOs;
using HbELearning.Application.Features.Certificates;
using HbELearning.Domain.Constants;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.Certificates;

/// <summary>
/// Danh sách chứng nhận để in (/certificate/register): Admin và phòng Nhân sự in cả công ty, còn cấp trưởng chỉ in được
/// chính mình và người thuộc quyền mình — cùng ranh giới với khối "nhân viên cấp dưới" trên trang chứng nhận.
/// </summary>
public class RegisterScopeTests
{
    private sealed record Org(ClassRoomWorld World, User Head, User Member, User Director, User OtherDeptMember, User HrStaff, int DeptId);

    /// <summary>Phòng A: Trưởng phòng, một học viên và Giám Đốc (bậc cao hơn). Phòng B: một học viên. Ai trong hai
    /// phòng đó cũng có một chứng nhận nội bộ. Phòng Nhân sự: một học viên, chưa có chứng nhận nào.</summary>
    private static Org Seed()
    {
        var world = ClassRoomWorld.Create();
        using var db = world.NewDb();
        int RoleId(string name) => db.Role.Single(r => r.Name == name).Id;

        var deptA = new Department { Code = "A", Name = "Phòng A" };
        var deptB = new Department { Code = "B", Name = "Phòng B" };
        var deptHr = new Department { Code = "NS", Name = "Phòng Nhân sự", IsHrDepartment = true };
        db.Department.AddRange(deptA, deptB, deptHr);
        db.SaveChanges();

        User Person(string code, string role, int deptId) => new()
        {
            EmployeeCode = code, FullName = "Người " + code, Email = $"{code.ToLowerInvariant()}@test.local",
            PasswordHash = "x", RoleId = RoleId(role), DepartmentId = deptId,
        };
        var head = Person("HEAD", RoleName.HeadOfDepartment, deptA.Id);
        var member = Person("MEM", RoleName.Learner, deptA.Id);
        var director = Person("DIR", RoleName.Director, deptA.Id);
        var other = Person("OTH", RoleName.Learner, deptB.Id);
        var hrStaff = Person("HR1", RoleName.Learner, deptHr.Id);
        db.User.AddRange(head, member, director, other, hrStaff);

        var course = new Course
        {
            Code = "C1", Title = "An toàn lao động", Status = CourseStatus.Published, CreatedById = world.Admin.Id,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
        };
        db.Course.Add(course);
        db.SaveChanges();

        var no = 0;
        foreach (var user in new[] { head, member, director, other })
        {
            no++;
            var enrollment = new Enrollment { UserId = user.Id, CourseId = course.Id, Status = EnrollmentStatus.Completed };
            db.Enrollment.Add(enrollment);
            db.SaveChanges();
            db.Certificate.Add(new Certificate
            {
                EnrollmentId = enrollment.Id, CertificateNo = $"HBE-2026-{no:000000}", VerificationCode = $"v{no}", FinalScore = 90,
            });
        }
        db.SaveChanges();
        return new Org(world, head, member, director, other, hrStaff, deptA.Id);
    }

    private static async Task<List<CertificateRegisterRowDto>> Register(ClassRoomWorld world, int? departmentId = null)
    {
        var result = await world.Service<ICertificateService>().GetRegister(null, departmentId, null, null, null);
        Assert.True(result.IsSuccess);
        return Assert.IsAssignableFrom<IEnumerable<CertificateRegisterRowDto>>(result.Value).ToList();
    }

    [Fact]
    public async Task A_head_of_department_prints_only_themselves_and_their_subordinates()
    {
        var org = Seed();
        org.World.ActAs(org.Head);

        var codeList = (await Register(org.World)).Select(x => x.EmployeeCode).Order().ToList();

        Assert.Equal(new[] { "HEAD", "MEM" }, codeList);
    }

    [Fact]
    public async Task Picking_another_department_does_not_widen_a_heads_scope()
    {
        var org = Seed();
        org.World.ActAs(org.Head);

        Assert.Empty(await Register(org.World, org.OtherDeptMember.DepartmentId));
    }

    [Fact]
    public async Task Hr_staff_print_the_whole_company_even_as_a_learner()
    {
        var org = Seed();
        org.World.ActAs(org.HrStaff);

        Assert.Equal(4, (await Register(org.World)).Count);
    }

    [Fact]
    public async Task An_ordinary_employee_cannot_print()
    {
        var org = Seed();
        org.World.ActAs(org.Member);

        var result = await org.World.Service<ICertificateService>().GetRegister(null, null, null, null, null);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Admin_still_prints_the_whole_company()
    {
        var org = Seed();
        org.World.ActAs(org.World.Admin);

        Assert.Equal(4, (await Register(org.World)).Count);
    }
}
