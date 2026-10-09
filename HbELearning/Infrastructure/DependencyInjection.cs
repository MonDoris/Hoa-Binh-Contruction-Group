// File: Infrastructure/DependencyInjection.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Infrastructure;

/// <summary>Ráp mọi lớp hiện thực của Infrastructure vào các cổng đã khai báo bên Application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, string[] args)
    {
        // ---------- Database (Sqlite / SqlServer / Oracle - đổi trong appsettings.json) ----------
        var dbProvider = DbProviderSetup.Resolve(configuration, args);
        services.AddSingleton(new DatabaseProviderName(dbProvider));

        // Khóa học / bài học vừa đổi thì báo Pico B học lại ngay (xem CourseKnowledgeChangeInterceptor).
        services.AddSingleton<ICourseKnowledgeSyncQueue, CourseKnowledgeSyncQueue>();
        services.AddSingleton<CourseKnowledgeChangeInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            DbProviderSetup.Apply(options, dbProvider, configuration);
            options.AddInterceptors(sp.GetRequiredService<CourseKnowledgeChangeInterceptor>());
        });

        // Lớp Application chỉ biết tới cổng IAppDbContext; đây là chỗ duy nhất nối nó với lớp thật.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // ---------- Danh tính ----------
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        // ---------- Tệp tin & tài liệu ----------
        services.AddScoped<IFileService, FileService>();
        // Kho gói SCORM (giải nén, đọc manifest, đóng gói xuất) — không giữ trạng thái nên dùng chung một bản.
        services.AddSingleton<IScormPackageStorage, ScormPackageStorage>();

        // Bộ lọc HTML không giữ trạng thái và dựng khá tốn (nạp cấu hình AngleSharp) nên đăng ký một bản
        // dùng chung cho cả tiến trình.
        services.AddSingleton<IHtmlSanitizer, LessonHtmlSanitizer>();
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IMediaFileProcessor, MediaFileProcessor>();
        services.AddScoped<ICertificateQrCodeGenerator, CertificateQrCodeGenerator>();
        services.AddScoped<IWordHtmlConverter, WordHtmlConverter>();
        services.AddScoped<IPowerPointHtmlConverter, PowerPointHtmlConverter>();
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        services.AddSingleton<IImportFileReader, ImportFileReader>();
        services.AddSingleton<IWordQuestionParser, WordQuestionParser>();
        services.AddSingleton<IImportTemplateWriter, ImportTemplateWriter>();

        // ---------- Thông báo ra ngoài ----------
        services.AddScoped<IEmailService, EmailService>();
        services.AddHttpClient<IZaloZnsService, ZaloZnsService>();
        services.AddHttpClient<IEsmsService, EsmsService>();

        // ---------- AI (Ollama) ----------
        services.AddHttpClient<IAiContentService, OllamaContentService>();
        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
        services.AddHttpClient<IAiChatService, OllamaChatService>();
        services.AddHttpClient<ICertificateReader, OllamaCertificateReader>();

        // ---------- Vòng lặp nền ----------
        // Ban cac loi nhac hoc tap nguoi hoc tu hen gio (xem StudyReminderWorker). Khac voi nhac nho theo
        // han hoan thanh — thu chi sinh ra luc nguoi hoc mo ung dung — cai nay chay cung may chu nen toi
        // dung phut da hen la co thong bao, du ho khong mo trang nao.
        services.AddHostedService<StudyReminderWorker>();
        // Để AI tự học nội dung các khóa học đã xuất bản — ngay khi xuất bản/sửa bài, và quét lại định kỳ
        // (xem CourseKnowledgeSyncWorker).
        services.AddHostedService<CourseKnowledgeSyncWorker>();
        // AI đối chiếu chứng nhận bên ngoài chưa có kết quả quét sẵn (xem ExternalCertificateVerifyWorker).
        services.AddSingleton<IExternalCertificateVerifyQueue, ExternalCertificateVerifyQueue>();
        services.AddHostedService<ExternalCertificateVerifyWorker>();

        return services;
    }
}
