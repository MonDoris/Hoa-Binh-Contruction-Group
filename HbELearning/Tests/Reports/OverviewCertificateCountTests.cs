using HbELearning.Application.DTOs;
using HbELearning.Application.Features.Reports;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.Reports;

/// <summary>
/// Ô "Chứng chỉ của bạn" trên trang Tổng quan (/report/overview → TotalCertificate) = chứng nhận nội bộ còn hiệu lực
/// + chứng nhận bên ngoài ĐÃ ĐƯỢC PHÒNG NHÂN SỰ XÁC NHẬN. Giấy mới qua AI, đang chờ hay bị từ chối không tính.
/// </summary>
public class OverviewCertificateCountTests
{
    private static ExternalCertificate External(int userId, string title) => new()
    {
        UserId = userId, Title = title, Issuer = "DOL English", IssuedAt = new DateTime(2026, 10, 1),
        FilePath = $"external-certificate/{title}.pdf", FileName = "x.pdf", ContentType = "application/pdf", FileSize = 10,
    };

    private static async Task<int> MyCertificateCount(ClassRoomWorld world, User viewer)
    {
        world.ActAs(viewer);
        var result = await world.Service<IReportService>().GetOverview();
        Assert.True(result.IsSuccess);
        return Assert.IsType<OverviewReportDto>(result.Value).TotalCertificate;
    }

    [Fact]
    public async Task Counts_internal_plus_only_hr_confirmed_external_certificates()
    {
        var world = ClassRoomWorld.Create();
        var me = world.Student;
        using (var db = world.NewDb())
        {
            var course = new Course
            {
                Code = "C1", Title = "An toàn", Status = CourseStatus.Published, CreatedById = world.Admin.Id,
                StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
            };
            db.Course.Add(course);
            db.SaveChanges();
            var enrollment = new Enrollment { UserId = me.Id, CourseId = course.Id, Status = EnrollmentStatus.Completed };
            db.Enrollment.Add(enrollment);
            db.SaveChanges();
            db.Certificate.Add(new Certificate { EnrollmentId = enrollment.Id, CertificateNo = "HBE-2026-000001", VerificationCode = "v1", FinalScore = 90 });

            var hrConfirmed = External(me.Id, "da-xac-nhan");
            hrConfirmed.ApproveByAi(DateTime.Now);
            hrConfirmed.ConfirmByHr(world.Host.Id, DateTime.Now);
            var aiOnly = External(me.Id, "chi-ai");
            aiOnly.ApproveByAi(DateTime.Now);
            var pending = External(me.Id, "dang-cho");
            var someoneElse = External(world.Outsider.Id, "nguoi-khac");
            someoneElse.ApproveByAi(DateTime.Now);
            someoneElse.ConfirmByHr(world.Host.Id, DateTime.Now);
            db.ExternalCertificate.AddRange(hrConfirmed, aiOnly, pending, someoneElse);
            db.SaveChanges();
        }

        // 1 nội bộ + 1 bên ngoài đã được Nhân sự xác nhận; giấy chỉ AI xác nhận / đang chờ / của người khác không tính.
        Assert.Equal(2, await MyCertificateCount(world, me));
    }
}
