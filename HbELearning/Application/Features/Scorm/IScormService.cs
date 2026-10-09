// File: Application/Features/Scorm/IScormService.cs

namespace HbELearning.Application.Features.Scorm;

/// <summary>Toàn bộ vòng đời một gói SCORM trong hệ thống: nhập (đầu vào) → phát và theo dõi theo chuẩn RTE + SN
/// (lúc học) → báo cáo, xuất gói (đầu ra).</summary>
public interface IScormService
{
    // ----- Người soạn / người duyệt -----
    Task<Result> GetByCourse(int courseId);
    Task<Result> GetById(int packageId);
    Task<Result> ImportToCourse(int courseId, ScormPackageImportRequest request, FileUpload? file);
    Task<Result> ImportAsCourse(ScormCourseImportRequest request, FileUpload? file);
    Task<Result> Update(int packageId, ScormPackageUpdateRequest request);
    Task<Result> Delete(int packageId);
    Task<Result> DownloadPackage(int packageId);

    // ----- Phiên học (trình phát) -----
    Task<Result> Launch(ScormLaunchRequest request);
    Task<Result> Commit(int registrationId, ScormCommitRequest request);
    Task<Result> Navigate(int registrationId, ScormNavigateRequest request);

    // ----- Báo cáo -----
    Task<Result> GetReport(int courseId);
    Task<Result> GetRegistrationDetail(int registrationId);
    Task<Result> ExportReportCsv(int courseId);

    // ----- Xuất khóa học ra gói SCORM 2004 4th Edition -----
    Task<Result> GetExportPreview(int courseId);

    /// <summary>imsmanifest.xml của khóa học — đúng bản sẽ nằm trong gói xuất.</summary>
    Task<Result> GetManifest(int courseId);
    Task<Result> ExportCourse(int courseId);
}
