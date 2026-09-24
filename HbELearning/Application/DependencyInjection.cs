// File: Application/DependencyInjection.cs

namespace HbELearning.Application;

/// <summary>Đăng ký các nghiệp vụ thuần của lớp Application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILearningService, LearningService>();
        services.AddScoped<IRankingEngine, RankingEngine>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<ICourseKnowledgeService, CourseKnowledgeService>();
        services.AddScoped<ICertificateIssuer, CertificateIssuer>();
        services.AddScoped<IDataImportService, DataImportService>();
        services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
        services.AddScoped<IStudyReminderDispatcher, StudyReminderDispatcher>();


        // ----- Ca sử dụng của từng chức năng (Application/Features) -----
        services.AddScoped<IAiDocumentService, AiDocumentService>();
        services.AddScoped<IAiQuickReplyService, AiQuickReplyService>();
        services.AddScoped<IAiService, AiService>();
        services.AddScoped<IAiTutorService, AiTutorService>();
        services.AddScoped<IApprovalDelegateService, ApprovalDelegateService>();
        services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<ILearningPathService, LearningPathService>();
        services.AddScoped<ILessonNoteService, LessonNoteService>();
        services.AddScoped<ILessonService, LessonService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IOnlineClassRoomService, OnlineClassRoomService>();
        services.AddScoped<IClassRoomSessionService, ClassRoomSessionService>();
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<IQuizService, QuizService>();
        services.AddScoped<IRankingService, RankingService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IScenarioService, ScenarioService>();
        services.AddScoped<IShopService, ShopService>();
        services.AddScoped<ISimAssetService, SimAssetService>();
        services.AddScoped<IStudyReminderService, StudyReminderService>();
        services.AddScoped<ITranslationService, TranslationService>();
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IVideoLearningService, VideoLearningService>();

        // QuickReplyMatcher là lớp static thuần (không trạng thái, không phụ thuộc) nên không đăng ký ở đây —
        // AiQuickReplyService và AiTutorService gọi thẳng.

        return services;
    }
}
