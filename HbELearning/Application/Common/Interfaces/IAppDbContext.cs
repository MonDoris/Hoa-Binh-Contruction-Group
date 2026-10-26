// File: Application/Common/Interfaces/IAppDbContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Cổng để lớp Application đọc/ghi dữ liệu mà KHÔNG nhìn thấy AppDbContext.
///
/// Vì sao phải có: AppDbContext nằm ở Infrastructure, mà Application không được phép tham chiếu
/// Infrastructure — đó là quy tắc phụ thuộc của Clean Architecture, và ở dự án này nó do trình biên
/// dịch cưỡng chế (HbELearning.Application.csproj cố ý không tham chiếu Infrastructure). Nghiệp vụ vì
/// vậy nhận vào IAppDbContext; AppDbContext chỉ là một lớp hiện thực của nó, ráp vào lúc chạy.
///
/// Danh sách DbSet dưới đây phải khớp với AppDbContext. Thêm thực thể mới thì khai báo ở CẢ HAI nơi —
/// thiếu ở đây thì nghiệp vụ không đụng được tới bảng mới, và lỗi chỉ lộ ra lúc biên dịch nơi gọi.
/// </summary>
public interface IAppDbContext
{
    DbSet<Role> Role { get; }
    DbSet<Department> Department { get; }
    DbSet<Position> Position { get; }
    DbSet<User> User { get; }
    DbSet<Course> Course { get; }
    DbSet<CourseTarget> CourseTarget { get; }
    DbSet<CourseApprovalHistory> CourseApprovalHistory { get; }
    DbSet<Lesson> Lesson { get; }
    DbSet<LearningPath> LearningPath { get; }
    DbSet<PathItem> PathItem { get; }
    DbSet<Quiz> Quiz { get; }
    DbSet<Question> Question { get; }
    DbSet<Answer> Answer { get; }
    DbSet<Enrollment> Enrollment { get; }
    DbSet<LessonProgress> LessonProgress { get; }
    DbSet<QuizAttempt> QuizAttempt { get; }
    DbSet<AttemptDetail> AttemptDetail { get; }
    DbSet<ScenarioScene> ScenarioScene { get; }
    DbSet<ScenarioNode> ScenarioNode { get; }
    DbSet<ScenarioCharacter> ScenarioCharacter { get; }
    DbSet<ScenarioChoice> ScenarioChoice { get; }
    DbSet<ScenarioAttempt> ScenarioAttempt { get; }
    DbSet<ScenarioAttemptDetail> ScenarioAttemptDetail { get; }
    DbSet<Certificate> Certificate { get; }
    DbSet<Notification> Notification { get; }
    DbSet<StudyReminder> StudyReminder { get; }
    DbSet<ApprovalDelegate> ApprovalDelegate { get; }
    DbSet<ApprovalWorkflow> ApprovalWorkflow { get; }
    DbSet<ApprovalWorkflowStep> ApprovalWorkflowStep { get; }
    DbSet<CourseStepApprover> CourseStepApprover { get; }
    DbSet<PasswordResetRequest> PasswordResetRequest { get; }
    DbSet<ZaloOAuthToken> ZaloOAuthToken { get; }
    DbSet<RankTier> RankTier { get; }
    DbSet<RankVoucherCode> RankVoucherCode { get; }
    DbSet<UserPointLog> UserPointLog { get; }
    DbSet<UserRankAward> UserRankAward { get; }
    DbSet<DepartmentRankReward> DepartmentRankReward { get; }
    DbSet<WeeklyMission> WeeklyMission { get; }
    DbSet<ShopProduct> ShopProduct { get; }
    DbSet<ShopRedemption> ShopRedemption { get; }
    DbSet<ExternalCertificate> ExternalCertificate { get; }
    DbSet<ScormPackage> ScormPackage { get; }
    DbSet<ScormRegistration> ScormRegistration { get; }
    DbSet<ScormRuntime> ScormRuntime { get; }
    DbSet<ShopPromotion> ShopPromotion { get; }
    DbSet<ShopPromotionItem> ShopPromotionItem { get; }
    DbSet<PointRule> PointRule { get; }
    DbSet<UserDailyStudy> UserDailyStudy { get; }
    DbSet<LessonNote> LessonNote { get; }
    DbSet<OnlineClassRoom> OnlineClassRoom { get; }
    DbSet<OnlineClassRoomEvent> OnlineClassRoomEvent { get; }
    DbSet<OnlineClassRoomRecording> OnlineClassRoomRecording { get; }
    DbSet<VideoQuestion> VideoQuestion { get; }
    DbSet<VideoQuestionOption> VideoQuestionOption { get; }
    DbSet<VideoQuestionAnswer> VideoQuestionAnswer { get; }
    DbSet<VideoChapter> VideoChapter { get; }
    DbSet<DocumentChunk> DocumentChunk { get; }
    DbSet<AiChatSession> AiChatSession { get; }
    DbSet<AiChatMessage> AiChatMessage { get; }
    DbSet<AiQuickReply> AiQuickReply { get; }
    DbSet<SimAsset> SimAsset { get; }
    DbSet<SimBuiltinScene> SimBuiltinScene { get; }
    DbSet<Translation> Translation { get; }
    DbSet<AppLanguage> AppLanguage { get; }
    DbSet<Conversation> Conversation { get; }
    DbSet<ConversationMember> ConversationMember { get; }
    DbSet<ChatMessage> ChatMessage { get; }

    DbSet<CourseFeedback> CourseFeedback { get; }

    // ----- Phần ngoài DbSet mà nghiệp vụ đang dùng tới -----

    /// <summary>Bộ theo dõi thay đổi của EF Core (xem CourseKnowledgeService dùng để tắt theo dõi khi nạp khối lượng lớn).</summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>Truy cập trạng thái theo dõi của một thực thể cụ thể.</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>Ghi mọi thay đổi đang chờ xuống database.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
