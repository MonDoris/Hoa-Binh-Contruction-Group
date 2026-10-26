// File: Infrastructure/Persistence/AppDbContext.cs
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HbELearning.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ---------- DbSet (đặt tên số ít theo quy ước dự án) ----------
    public DbSet<Role> Role => Set<Role>();
    public DbSet<Department> Department => Set<Department>();
    public DbSet<Position> Position => Set<Position>();
    public DbSet<User> User => Set<User>();
    public DbSet<Course> Course => Set<Course>();
    public DbSet<CourseTarget> CourseTarget => Set<CourseTarget>();
    public DbSet<CourseApprovalHistory> CourseApprovalHistory => Set<CourseApprovalHistory>();
    public DbSet<Lesson> Lesson => Set<Lesson>();
    public DbSet<LearningPath> LearningPath => Set<LearningPath>();
    public DbSet<PathItem> PathItem => Set<PathItem>();
    public DbSet<Quiz> Quiz => Set<Quiz>();
    public DbSet<Question> Question => Set<Question>();
    public DbSet<Answer> Answer => Set<Answer>();
    public DbSet<Enrollment> Enrollment => Set<Enrollment>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    public DbSet<QuizAttempt> QuizAttempt => Set<QuizAttempt>();
    public DbSet<AttemptDetail> AttemptDetail => Set<AttemptDetail>();
    public DbSet<ScenarioScene> ScenarioScene => Set<ScenarioScene>();
    public DbSet<ScenarioNode> ScenarioNode => Set<ScenarioNode>();

    /// <summary>Nhân vật được đặt vào cảnh 3D của mô phỏng. Xem <see cref="ScenarioCharacter"/>.</summary>
    public DbSet<ScenarioCharacter> ScenarioCharacter => Set<ScenarioCharacter>();
    public DbSet<ScenarioChoice> ScenarioChoice => Set<ScenarioChoice>();
    public DbSet<ScenarioAttempt> ScenarioAttempt => Set<ScenarioAttempt>();
    public DbSet<ScenarioAttemptDetail> ScenarioAttemptDetail => Set<ScenarioAttemptDetail>();
    public DbSet<Certificate> Certificate => Set<Certificate>();
    public DbSet<Notification> Notification => Set<Notification>();

    /// <summary>Lời nhắc học tập người học tự hẹn giờ trên Lịch học. Xem <see cref="StudyReminder"/>.</summary>
    public DbSet<StudyReminder> StudyReminder => Set<StudyReminder>();
    public DbSet<ApprovalDelegate> ApprovalDelegate => Set<ApprovalDelegate>();

    /// <summary>Luồng cấp duyệt do Admin định nghĩa, người soạn nội dung chọn cho khóa học. Xem <see cref="ApprovalWorkflow"/>.</summary>
    public DbSet<ApprovalWorkflow> ApprovalWorkflow => Set<ApprovalWorkflow>();
    public DbSet<ApprovalWorkflowStep> ApprovalWorkflowStep => Set<ApprovalWorkflowStep>();

    /// <summary>Người duyệt được người soạn chỉ định cho từng bước của từng khóa học. Xem <see cref="CourseStepApprover"/>.</summary>
    public DbSet<CourseStepApprover> CourseStepApprover => Set<CourseStepApprover>();
    public DbSet<PasswordResetRequest> PasswordResetRequest => Set<PasswordResetRequest>();
    public DbSet<ZaloOAuthToken> ZaloOAuthToken => Set<ZaloOAuthToken>();
    public DbSet<RankTier> RankTier => Set<RankTier>();
    public DbSet<RankVoucherCode> RankVoucherCode => Set<RankVoucherCode>();
    public DbSet<UserPointLog> UserPointLog => Set<UserPointLog>();
    public DbSet<UserRankAward> UserRankAward => Set<UserRankAward>();
    public DbSet<DepartmentRankReward> DepartmentRankReward => Set<DepartmentRankReward>();
    public DbSet<WeeklyMission> WeeklyMission => Set<WeeklyMission>();

    // Cửa hàng điểm thưởng: món quà bày bán và từng lượt người học đổi điểm lấy quà.
    public DbSet<ShopProduct> ShopProduct => Set<ShopProduct>();
    public DbSet<ShopRedemption> ShopRedemption => Set<ShopRedemption>();
    public DbSet<ExternalCertificate> ExternalCertificate => Set<ExternalCertificate>();
    // Gói SCORM, bản ghi học gói (registration) và dữ liệu run-time từng SCO.
    public DbSet<ScormPackage> ScormPackage => Set<ScormPackage>();
    public DbSet<ScormRegistration> ScormRegistration => Set<ScormRegistration>();
    public DbSet<ScormRuntime> ScormRuntime => Set<ScormRuntime>();
    public DbSet<ShopPromotion> ShopPromotion => Set<ShopPromotion>();
    public DbSet<ShopPromotionItem> ShopPromotionItem => Set<ShopPromotionItem>();
    public DbSet<PointRule> PointRule => Set<PointRule>();
    public DbSet<UserDailyStudy> UserDailyStudy => Set<UserDailyStudy>();
    public DbSet<LessonNote> LessonNote => Set<LessonNote>();
    public DbSet<OnlineClassRoom> OnlineClassRoom => Set<OnlineClassRoom>();
    public DbSet<OnlineClassRoomEvent> OnlineClassRoomEvent => Set<OnlineClassRoomEvent>();
    public DbSet<OnlineClassRoomRecording> OnlineClassRoomRecording => Set<OnlineClassRoomRecording>();
    public DbSet<VideoQuestion> VideoQuestion => Set<VideoQuestion>();
    public DbSet<VideoQuestionOption> VideoQuestionOption => Set<VideoQuestionOption>();
    public DbSet<VideoQuestionAnswer> VideoQuestionAnswer => Set<VideoQuestionAnswer>();
    public DbSet<VideoChapter> VideoChapter => Set<VideoChapter>();

    /// <summary>Kho tri thức: các đoạn văn bản tách từ tài liệu PDF/Word kèm vector nhúng (Ollama).</summary>
    public DbSet<DocumentChunk> DocumentChunk => Set<DocumentChunk>();

    /// <summary>Lịch sử hỏi đáp với HB Minerva — mỗi người một danh sách hội thoại riêng.</summary>
    public DbSet<AiChatSession> AiChatSession => Set<AiChatSession>();
    public DbSet<AiChatMessage> AiChatMessage => Set<AiChatMessage>();

    /// <summary>Câu trả lời cài sẵn của HB Minerva (chào hỏi, cảm ơn...) — trả lời thẳng, không qua model.</summary>
    public DbSet<AiQuickReply> AiQuickReply => Set<AiQuickReply>();

    /// <summary>Thư viện bối cảnh 3D dùng chung (model .glb, ảnh/video 360°, ảnh phẳng) cho mô phỏng.</summary>
    public DbSet<SimAsset> SimAsset => Set<SimAsset>();

    /// <summary>Các bối cảnh 3D dựng sẵn bằng code (công trường, văn phòng Nhân sự) — cũng là mục của Thư
    /// viện 3D, chỉ khác là không có tệp đính kèm. Xem <see cref="SimBuiltinScene"/>.</summary>
    public DbSet<SimBuiltinScene> SimBuiltinScene => Set<SimBuiltinScene>();

    /// <summary>Bảng dịch giao diện, gồm cả bản xuất xưởng nạp từ wwwroot/locales lẫn từ chuyên ngành do
    /// công ty tự thêm ở màn hình "Dịch ngôn ngữ". Xem <see cref="Translation"/>.</summary>
    public DbSet<Translation> Translation => Set<Translation>();

    /// <summary>Các ngôn ngữ giao diện của hệ thống. Xem <see cref="AppLanguage"/>.</summary>
    public DbSet<AppLanguage> AppLanguage => Set<AppLanguage>();

    // Nhắn tin nội bộ giữa các tài khoản.
    public DbSet<Conversation> Conversation => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMember => Set<ConversationMember>();
    public DbSet<ChatMessage> ChatMessage => Set<ChatMessage>();
    public DbSet<CourseFeedback> CourseFeedback => Set<CourseFeedback>();


    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ================= UNIQUE INDEX =================
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        // Xóa vai trò cha thì KHÔNG kéo theo vai trò con (NoAction): con vẫn còn người dùng thuộc về nó, mất
        // theo là mất luôn tài khoản. Thực tế cha luôn là vai trò hệ thống — vốn đã bị cấm xóa.
        b.Entity<Role>().HasOne(x => x.ParentRole).WithMany()
            .HasForeignKey(x => x.ParentRoleId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Department>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Position>().HasIndex(x => x.Code).IsUnique();
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<User>().HasIndex(x => x.EmployeeCode).IsUnique();
        // Tên tài khoản là một cách đăng nhập, nên phải là duy nhất — nhưng đại đa số tài khoản để trống.
        // SqlServer coi mọi NULL trong chỉ mục duy nhất là TRÙNG nhau (chỉ chứa nổi một dòng trống), nên phải
        // lọc bỏ NULL. Sqlite/Oracle theo chuẩn SQL (NULL khác NULL) và Oracle không hỗ trợ chỉ mục có lọc.
        var userNameIndex = b.Entity<User>().HasIndex(x => x.UserName).IsUnique();
        if (Database.IsSqlServer()) userNameIndex.HasFilter("[USER_NAME] IS NOT NULL");
        b.Entity<Course>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Certificate>().HasIndex(x => x.CertificateNo).IsUnique();
        b.Entity<Certificate>().HasIndex(x => x.VerificationCode).IsUnique();
        // Mỗi bối cảnh dựng sẵn chỉ có ĐÚNG một bản ghi mô tả: hai bản ghi cùng SceneKey thì thư viện hiện
        // trùng và không biết tên nào mới là tên đang dùng.
        b.Entity<SimBuiltinScene>().HasIndex(x => x.SceneKey).IsUnique();
        // Một khóa chỉ có ĐÚNG một câu chữ trong mỗi ngôn ngữ. Không có ràng buộc này thì hai dòng cùng
        // khóa sẽ tranh nhau, và câu nào thắng phụ thuộc thứ tự đọc của truy vấn — sai ngẫu nhiên.
        b.Entity<Translation>().HasIndex(x => new { x.ResourceKey, x.Lang }).IsUnique();
        b.Entity<AppLanguage>().HasIndex(x => x.Code).IsUnique();

        // Một người học chỉ có 1 bản ghi phân công cho mỗi khóa học
        b.Entity<Enrollment>().HasIndex(x => new { x.UserId, x.CourseId }).IsUnique();
        // Một bản ghi tiến độ duy nhất cho mỗi (enrollment, lesson)
        b.Entity<LessonProgress>().HasIndex(x => new { x.EnrollmentId, x.LessonId }).IsUnique();
        // Không trùng số lần làm bài
        b.Entity<QuizAttempt>().HasIndex(x => new { x.EnrollmentId, x.QuizId, x.AttemptNo }).IsUnique();
        // Không trùng đối tượng đào tạo mục tiêu
        b.Entity<CourseTarget>().HasIndex(x => new { x.CourseId, x.DepartmentId, x.PositionId }).IsUnique();
        // Một người chỉ được ủy quyền duyệt 1 lần tại một thời điểm
        b.Entity<ApprovalDelegate>().HasIndex(x => x.UserId).IsUnique();
        // Ten luong cap duyet khong trung nhau - nguoi soan chon luong bang TEN trong o xo xuong, hai luong
        // cung ten thi khong co cach nao biet minh vua chon cai nao.
        b.Entity<ApprovalWorkflow>().HasIndex(x => x.Name).IsUnique();
        // Khong trung thu tu buoc trong mot luong
        b.Entity<ApprovalWorkflowStep>().HasIndex(x => new { x.ApprovalWorkflowId, x.OrderIndex }).IsUnique();
        // Mỗi bước của một khóa học chỉ chỉ định được ĐÚNG một người duyệt — có hai dòng thì "ai được duyệt
        // bước này" phụ thuộc thứ tự đọc của truy vấn, sai ngẫu nhiên.
        b.Entity<CourseStepApprover>().HasIndex(x => new { x.CourseId, x.StepOrder }).IsUnique();
        // Mã tham gia phòng học online không được trùng nhau
        b.Entity<OnlineClassRoom>().HasIndex(x => x.JoinCode).IsUnique();
        // Không trùng thứ tự trong lộ trình
        b.Entity<PathItem>().HasIndex(x => new { x.LearningPathId, x.OrderIndex }).IsUnique();
        // Không trùng số lần chơi mô phỏng
        b.Entity<ScenarioAttempt>().HasIndex(x => new { x.EnrollmentId, x.ScenarioSceneId, x.AttemptNo }).IsUnique();
        // Mỗi điểm nguy cơ chỉ trả lời 1 lần trong 1 lượt chơi
        b.Entity<ScenarioAttemptDetail>().HasIndex(x => new { x.ScenarioAttemptId, x.ScenarioNodeId }).IsUnique();
        // Mỗi hoạt động (bài học/bài kiểm tra/khóa học) chỉ cộng điểm xếp hạng đúng 1 lần cho 1 người dùng
        b.Entity<UserPointLog>().HasIndex(x => new { x.UserId, x.Reason, x.SourceType, x.SourceId }).IsUnique();
        // Mỗi người dùng chỉ đạt 1 bậc hạng đúng 1 lần
        b.Entity<UserRankAward>().HasIndex(x => new { x.UserId, x.RankTierId }).IsUnique();
        // Mỗi phòng ban chỉ có 1 phần thưởng riêng cho mỗi bậc hạng
        b.Entity<DepartmentRankReward>().HasIndex(x => new { x.RankTierId, x.DepartmentId }).IsUnique();
        // Mỗi loại hoạt động chỉ có ĐÚNG một mức điểm. Hai dòng cùng loại thì điểm cộng cho học viên phụ
        // thuộc dòng nào đọc trước — sai ngẫu nhiên và rất khó lần ra khi đã lẫn vào nhật ký điểm.
        b.Entity<PointRule>().HasIndex(x => x.Reason).IsUnique();
        // Mỗi người đúng một dòng cho mỗi ngày. Không có ràng buộc này thì hai nhịp heartbeat gần nhau sẽ
        // cùng thấy "chưa có dòng hôm nay" và tạo ra hai dòng — thời gian học trong ngày bị chia đôi và
        // không bao giờ chạm ngưỡng cộng điểm.
        b.Entity<UserDailyStudy>().HasIndex(x => new { x.UserId, x.StudyDate }).IsUnique();

        // Cửa hàng bày mỗi tên món ĐÚNG một lần: hai dòng cùng tên thì người học không phân biệt được món
        // nào còn hàng, còn quản trị viên sửa giá một dòng lại tưởng đã sửa cả hai.
        b.Entity<ShopProduct>().HasIndex(x => x.Name).IsUnique();
        // Ba đường truy vấn của phần lượt đổi: lịch sử của một người, danh sách người đã đổi một món, và
        // hàng chờ trao quà của quản trị viên.
        b.Entity<ShopRedemption>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<ShopRedemption>().HasIndex(x => new { x.ShopProductId, x.CreatedAt });
        b.Entity<ShopRedemption>().HasIndex(x => x.Status);
        // Hai cách đọc bảng chứng nhận bên ngoài: "giấy của tôi" (theo người) và hàng chờ duyệt của Admin.
        b.Entity<ExternalCertificate>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<ExternalCertificate>().HasIndex(x => x.Status);
        // Mỗi đợt khuyến mãi một tên: hai đợt trùng tên thì nhãn trên thẻ hàng không nói được món đang
        // giảm vì đợt nào, mà đó chính là việc của cái nhãn.
        b.Entity<ShopPromotion>().HasIndex(x => x.Name).IsUnique();
        // Truy vấn nóng nhất của cả tính năng: "hôm nay đang có đợt nào chạy".
        b.Entity<ShopPromotion>().HasIndex(x => new { x.IsActive, x.StartAt, x.EndAt });
        // Một sản phẩm chỉ được tick vào một đợt ĐÚNG một lần — hai dòng trùng thì món đó bị đếm hai lần
        // trong màn hình quản trị mà giá vẫn chỉ giảm một lần, một sai lệch không có cách nào giải thích.
        b.Entity<ShopPromotionItem>().HasIndex(x => new { x.ShopPromotionId, x.ShopProductId }).IsUnique();

        // Ghi chú theo mốc thời gian: KHÔNG duy nhất — một học viên hoàn toàn có thể ghi nhiều ghi chú ở
        // cùng một giây (xem lại đoạn khó, nghĩ ra thêm ý). Chỉ đánh chỉ mục để lấy nhanh theo bài học.
        b.Entity<LessonNote>().HasIndex(x => new { x.EnrollmentId, x.LessonId, x.AtSecond });
        // Feedback: lấy theo khóa (tab Feedback / báo cáo) và theo người viết (tìm đánh giá của chính mình).
        // Không đặt duy nhất cho đánh giá: SqlServer/Sqlite/Oracle không chung cú pháp chỉ mục có điều kiện,
        // nên "mỗi người một đánh giá mỗi khóa" do FeedbackService.SaveReview giữ.
        b.Entity<CourseFeedback>().HasIndex(x => new { x.CourseId, x.Kind });
        b.Entity<CourseFeedback>().HasIndex(x => new { x.UserId, x.CourseId });
        // Mỗi mốc thời gian trong một video chỉ có 1 câu hỏi — tránh 2 hộp thoại bật lên cùng lúc
        b.Entity<VideoQuestion>().HasIndex(x => new { x.LessonId, x.AtSecond }).IsUnique();
        // Mỗi học viên chỉ giữ 1 bản ghi trả lời cho mỗi câu hỏi trong video (làm lại thì ghi đè)
        b.Entity<VideoQuestionAnswer>().HasIndex(x => new { x.EnrollmentId, x.VideoQuestionId }).IsUnique();
        // Hai chương của cùng một video không được bắt đầu ở cùng một giây. Đây mới chỉ chặn được trường hợp
        // trùng khít; ràng buộc thật là "các đoạn không chồng lấn nhau" thì CSDL không diễn tả nổi nên phải
        // kiểm trong VideoLearningService — index này chỉ là lớp chặn cuối phòng khi có nguồn ghi khác.
        b.Entity<VideoChapter>().HasIndex(x => new { x.LessonId, x.StartSecond }).IsUnique();

        // ================= ĐỘ CHÍNH XÁC SỐ THẬP PHÂN =================
        b.Entity<Course>().Property(x => x.PassScore).HasPrecision(5, 2);
        b.Entity<Quiz>().Property(x => x.PassScore).HasPrecision(5, 2);
        b.Entity<Question>().Property(x => x.Score).HasPrecision(6, 2);
        b.Entity<Enrollment>().Property(x => x.ProgressPercent).HasPrecision(5, 2);
        b.Entity<Enrollment>().Property(x => x.FinalScore).HasPrecision(5, 2);
        b.Entity<QuizAttempt>().Property(x => x.Score).HasPrecision(6, 2);
        b.Entity<QuizAttempt>().Property(x => x.MaxScore).HasPrecision(6, 2);
        b.Entity<AttemptDetail>().Property(x => x.EarnedScore).HasPrecision(6, 2);
        b.Entity<Certificate>().Property(x => x.FinalScore).HasPrecision(5, 2);
        b.Entity<ScenarioScene>().Property(x => x.PassScore).HasPrecision(5, 2);
        b.Entity<VideoQuestion>().Property(x => x.Score).HasPrecision(6, 2);
        b.Entity<VideoQuestionAnswer>().Property(x => x.EarnedScore).HasPrecision(6, 2);

        // Nội dung bài học dài -> kiểu cột phù hợp từng hệ quản trị
        var lessonContent = b.Entity<Lesson>().Property(x => x.Content);
        if (Database.ProviderName == "Oracle.EntityFrameworkCore")
            lessonContent.HasColumnType("NCLOB");
        else if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            lessonContent.HasColumnType("nvarchar(max)");
        // SQLite: dùng kiểu TEXT mặc định

        // ================= QUAN HỆ & HÀNH VI XÓA =================
        b.Entity<User>().HasOne(x => x.Role).WithMany(x => x.UserList)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<User>().HasOne(x => x.Department).WithMany(x => x.UserList)
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        // GHI CHÚ CHUNG VỀ "NoAction" TRONG PHẦN NÀY
        // SQL Server từ chối tạo bảng khi giữa hai bảng có NHIỀU HƠN MỘT đường lan truyền xóa
        // ("Introducing FOREIGN KEY constraint ... may cause cycles or multiple cascade paths"). Sqlite không
        // kiểm nên trước đây chạy được, nhưng đổi DatabaseProvider sang SqlServer là app chết ngay lúc dựng
        // schema. Những khóa ngoại để NoAction dưới đây là các nhánh phụ bị cắt để mỗi cặp bảng chỉ còn đúng
        // một đường; HÀNH VI XÓA KHÔNG ĐỔI, chỉ chuyển từ CSDL lo sang mã nguồn lo — chỗ nào cũng có ghi rõ.

        // Xóa phòng ban tới được User bằng hai đường: thẳng (User.Department) và vòng qua Position.
        // Giữ đường thẳng, cắt đường vòng. Không cần dọn thủ công: PositionService.Delete đã từ chối xóa
        // vị trí đang có nhân viên.
        b.Entity<User>().HasOne(x => x.Position).WithMany(x => x.UserList)
            .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<Position>().HasOne(x => x.Department).WithMany(x => x.PositionList)
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Course>().HasOne(x => x.CreatedBy).WithMany(x => x.CreatedCourseList)
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Course>().HasOne(x => x.HrApprover).WithMany()
            .HasForeignKey(x => x.HrApproverUserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<CourseTarget>().HasOne(x => x.Course).WithMany(x => x.CourseTargetList)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CourseTarget>().HasOne(x => x.Department).WithMany(x => x.CourseTargetList)
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Cascade);
        // Cùng lý do: Department -> CourseTarget (thẳng) và Department -> Position -> CourseTarget.
        // Việc xóa đối tượng đào tạo theo vị trí chuyển sang PositionService.Delete().
        b.Entity<CourseTarget>().HasOne(x => x.Position).WithMany(x => x.CourseTargetList)
            .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<CourseApprovalHistory>().HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CourseApprovalHistory>().HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Lesson>().HasOne(x => x.Course).WithMany(x => x.LessonList)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        // Course 1 - 1 LearningPath
        b.Entity<LearningPath>().HasOne(x => x.Course).WithOne(x => x.LearningPath)
            .HasForeignKey<LearningPath>(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        // Cascade (không Restrict): bước lộ trình tham chiếu bài học/bài kiểm tra đã bị xóa thì
        // tự dọn theo, tránh lỗi FOREIGN KEY constraint khi xóa bài học/bài kiểm tra/khóa học.
        b.Entity<PathItem>().HasOne(x => x.LearningPath).WithMany(x => x.PathItemList)
            .HasForeignKey(x => x.LearningPathId).OnDelete(DeleteBehavior.Cascade);
        // PathItem có tới BỐN cha cùng lan truyền từ Course (LearningPath, Lesson, Quiz, ScenarioScene) —
        // tức bốn đường Course -> PathItem. Chỉ giữ lan truyền từ LearningPath vì đó mới là chủ sở hữu thật
        // của một bước lộ trình; ba đường còn lại để NoAction.
        //
        // Hành vi không đổi: LessonService.Delete, QuizService.Delete và ScenarioService.Delete đều
        // đã tự xóa PathItem của mình TRƯỚC khi xóa đối tượng cha, từ trước khi có thay đổi này.
        b.Entity<PathItem>().HasOne(x => x.Lesson).WithMany(x => x.PathItemList)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<PathItem>().HasOne(x => x.Quiz).WithMany(x => x.PathItemList)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<PathItem>().HasOne(x => x.ScenarioScene).WithMany(x => x.PathItemList)
            .HasForeignKey(x => x.ScenarioSceneId).OnDelete(DeleteBehavior.NoAction);

        // Xóa người dùng KHÔNG được kéo theo tệp trong thư viện dùng chung: tệp là tài sản của công ty,
        // còn cột này chỉ ghi lại ai đã góp vào.
        b.Entity<SimAsset>().HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);

        // Cùng lý do: xóa người dùng không được kéo theo bản dịch của cả công ty, cột này chỉ ghi ai sửa gần nhất.
        b.Entity<Translation>().HasOne(x => x.UpdatedBy).WithMany()
            .HasForeignKey(x => x.UpdatedById).OnDelete(DeleteBehavior.SetNull);

        b.Entity<ScenarioScene>().HasOne(x => x.Course).WithMany(x => x.ScenarioSceneList)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        // SetNull (không Cascade): xóa bài học không tự xóa mô phỏng gắn theo — cùng lý do với Quiz.Lesson.
        // Giống hệt trường hợp Quiz ở trên — xem LessonService.Delete().
        b.Entity<ScenarioScene>().HasOne(x => x.Lesson).WithMany(x => x.ScenarioSceneList)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ScenarioNode>().HasOne(x => x.ScenarioScene).WithMany(x => x.NodeList)
            .HasForeignKey(x => x.ScenarioSceneId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScenarioCharacter>().HasOne(x => x.ScenarioScene).WithMany(x => x.CharacterList)
            .HasForeignKey(x => x.ScenarioSceneId).OnDelete(DeleteBehavior.Cascade);
        // Tra nhanh "tình huống nào đang gắn vào nhân vật này" khi xóa nhân vật hoặc khi dựng cảnh.
        b.Entity<ScenarioNode>().HasIndex(x => x.AttachedCharacterId);
        b.Entity<ScenarioChoice>().HasOne(x => x.ScenarioNode).WithMany(x => x.ChoiceList)
            .HasForeignKey(x => x.ScenarioNodeId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ScenarioAttempt>().HasOne(x => x.Enrollment).WithMany(x => x.ScenarioAttemptList)
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScenarioAttempt>().HasOne(x => x.ScenarioScene).WithMany(x => x.AttemptList)
            .HasForeignKey(x => x.ScenarioSceneId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<ScenarioAttemptDetail>().HasOne(x => x.ScenarioAttempt).WithMany(x => x.DetailList)
            .HasForeignKey(x => x.ScenarioAttemptId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScenarioAttemptDetail>().HasOne(x => x.ScenarioNode).WithMany()
            .HasForeignKey(x => x.ScenarioNodeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ScenarioAttemptDetail>().HasOne(x => x.ScenarioChoice).WithMany()
            .HasForeignKey(x => x.ScenarioChoiceId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Quiz>().HasOne(x => x.Course).WithMany(x => x.QuizList)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        // SetNull (không Cascade): xóa bài học không tự xóa bài kiểm tra gắn theo — bài kiểm tra tách rời
        // thành độc lập để tránh mất dữ liệu câu hỏi/lượt làm bài đã có.
        // Khóa học tới được Quiz bằng hai đường: thẳng (Quiz.Course) và vòng qua Lesson. Cắt đường vòng;
        // việc "xóa bài học thì gỡ bài kiểm tra khỏi bài đó" chuyển sang LessonService.Delete().
        b.Entity<Quiz>().HasOne(x => x.Lesson).WithMany(x => x.QuizList)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Question>().HasOne(x => x.Quiz).WithMany(x => x.QuestionList)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Answer>().HasOne(x => x.Question).WithMany(x => x.AnswerList)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Enrollment>().HasOne(x => x.User).WithMany(x => x.EnrollmentList)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Enrollment>().HasOne(x => x.Course).WithMany(x => x.EnrollmentList)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Enrollment>().HasOne(x => x.AssignedBy).WithMany(x => x.AssignedEnrollmentList)
            .HasForeignKey(x => x.AssignedById).OnDelete(DeleteBehavior.Restrict);

        b.Entity<LessonProgress>().HasOne(x => x.Enrollment).WithMany(x => x.LessonProgressList)
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LessonProgress>().HasOne(x => x.Lesson).WithMany(x => x.LessonProgressList)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<QuizAttempt>().HasOne(x => x.Enrollment).WithMany(x => x.QuizAttemptList)
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<QuizAttempt>().HasOne(x => x.Quiz).WithMany(x => x.QuizAttemptList)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<AttemptDetail>().HasOne(x => x.QuizAttempt).WithMany(x => x.AttemptDetailList)
            .HasForeignKey(x => x.QuizAttemptId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AttemptDetail>().HasOne(x => x.Question).WithMany(x => x.AttemptDetailList)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);

        // Enrollment 1 - 1 Certificate
        b.Entity<Certificate>().HasOne(x => x.Enrollment).WithOne(x => x.Certificate)
            .HasForeignKey<Certificate>(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Notification>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notification>().HasOne(x => x.Enrollment).WithMany()
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notification>().HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<StudyReminder>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // Hủy phân công khóa học thì lời nhắc gắn với nó cũng hết chỗ để dẫn tới — xóa theo.
        b.Entity<StudyReminder>().HasOne(x => x.Enrollment).WithMany()
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        // Dịch vụ nền quét bảng này mỗi phút, luôn lọc đúng hai cột này — thiếu index là quét toàn bảng.
        b.Entity<StudyReminder>().HasIndex(x => new { x.IsActive, x.RemindAt });

        b.Entity<PasswordResetRequest>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ApprovalDelegate>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApprovalDelegate>().HasOne(x => x.GrantedBy).WithMany()
            .HasForeignKey(x => x.GrantedById).OnDelete(DeleteBehavior.Restrict);

        b.Entity<ApprovalWorkflow>().HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        // Xoa phong ban ma van con luong duyet cua phong do thi luong mo coi - chan lai, buoc doi luong sang
        // phong khac (hoac ve dung chung) truoc.
        b.Entity<ApprovalWorkflow>().HasOne(x => x.Department).WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        // Xoa luong thi xoa luon cac buoc cua no (buoc khong co y nghia khi dung mot minh).
        b.Entity<ApprovalWorkflowStep>().HasOne(x => x.ApprovalWorkflow).WithMany(x => x.StepList)
            .HasForeignKey(x => x.ApprovalWorkflowId).OnDelete(DeleteBehavior.Cascade);
        // Vai tro / phong ban dang duoc mot buoc duyet tro toi thi khong cho xoa - mat chung la luong duyet
        // gay giua chung ma khong ai hay.
        b.Entity<ApprovalWorkflowStep>().HasOne(x => x.Role).WithMany()
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApprovalWorkflowStep>().HasOne(x => x.Department).WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        // Xoa luong KHONG keo theo khoa hoc dang dung no - ApprovalWorkflowService da chan xoa luong con
        // khoa hoc tham chieu, rang buoc nay la lop chan cuoi cung o tang CSDL.
        b.Entity<Course>().HasOne(x => x.ApprovalWorkflow).WithMany()
            .HasForeignKey(x => x.ApprovalWorkflowId).OnDelete(DeleteBehavior.Restrict);

        // Xoa khoa hoc thi xoa luon cac chi dinh nguoi duyet cua no (khong con y nghia khi dung mot minh).
        b.Entity<CourseStepApprover>().HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        // Nguoi dang duoc chi dinh duyet thi khong cho xoa tai khoan - mat chung la buoc duyet do khong con
        // ai bam duoc nua. Restrict o ca hai khoa ngoai tro toi HBE_USER: SqlServer khong cho nhieu duong
        // cascade cung do ve mot bang.
        b.Entity<CourseStepApprover>().HasOne(x => x.ApproverUser).WithMany()
            .HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourseStepApprover>().HasOne(x => x.AssignedBy).WithMany()
            .HasForeignKey(x => x.AssignedById).OnDelete(DeleteBehavior.Restrict);

        b.Entity<RankVoucherCode>().HasOne(x => x.RankTier).WithMany(x => x.VoucherCodeList)
            .HasForeignKey(x => x.RankTierId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RankVoucherCode>().HasOne(x => x.AssignedToUser).WithMany()
            .HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<UserPointLog>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<UserRankAward>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserDailyStudy>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // Hủy phân công hoặc xóa bài học thì ghi chú đi theo — giữ lại chỉ còn là dòng chữ treo lơ lửng
        // không còn video nào để bấm về.
        // Bài học dùng NoAction: một đường xóa dây chuyền thứ hai tới cùng bảng ghi chú (Enrollment ->
        // Lesson) là thứ SqlServer từ chối tạo. Xóa bài học đã có ghi chú do LessonService lo.
        b.Entity<LessonNote>().HasOne(x => x.Enrollment).WithMany()
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LessonNote>().HasOne(x => x.Lesson).WithMany()
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
        // Feedback: chỉ Enrollment được lan truyền xóa (hủy phân công thì bình luận/đánh giá của người đó đi theo).
        // Course, User và Parent phải NoAction: xóa khóa/người dùng tới bảng này bằng nhiều đường (trực tiếp và
        // vòng qua Enrollment), còn Parent là tự tham chiếu — cả hai đều là thứ SqlServer từ chối tạo. Xóa khóa học
        // do CourseService.Delete dọn feedback trước; trả lời của bình luận bị xóa do FeedbackService.Delete lo.
        b.Entity<CourseFeedback>().HasOne(x => x.Enrollment).WithMany()
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CourseFeedback>().HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<CourseFeedback>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<CourseFeedback>().HasOne(x => x.Parent).WithMany()
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<UserRankAward>().HasOne(x => x.RankTier).WithMany(x => x.AwardList)
            .HasForeignKey(x => x.RankTierId).OnDelete(DeleteBehavior.Restrict);
        // Xóa người dùng tới được UserRankAward bằng hai đường: thẳng, và vòng qua RankVoucherCode
        // (mã trúng thưởng gán cho người đó). Cắt đường vòng. Không cần dọn thủ công:
        // RankingService.DeleteVoucherCode đã từ chối xóa mã đã cấp cho người học.
        b.Entity<UserRankAward>().HasOne(x => x.VoucherCode).WithMany()
            .HasForeignKey(x => x.VoucherCodeId).OnDelete(DeleteBehavior.NoAction);

        b.Entity<DepartmentRankReward>().HasOne(x => x.RankTier).WithMany()
            .HasForeignKey(x => x.RankTierId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DepartmentRankReward>().HasOne(x => x.Department).WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<OnlineClassRoom>().HasOne(x => x.HostUser).WithMany()
            .HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<OnlineClassRoomEvent>().HasOne(x => x.OnlineClassRoom).WithMany()
            .HasForeignKey(x => x.OnlineClassRoomId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OnlineClassRoomEvent>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<OnlineClassRoomRecording>().HasOne(x => x.OnlineClassRoom).WithMany()
            .HasForeignKey(x => x.OnlineClassRoomId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OnlineClassRoomRecording>().HasOne(x => x.RecordedByUser).WithMany()
            .HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Xóa bài học thì xóa luôn câu hỏi chèn trong video của bài học đó (câu hỏi không có ý nghĩa
        // độc lập, khác Quiz — nên Cascade chứ không SetNull).
        b.Entity<VideoQuestion>().HasOne(x => x.Lesson).WithMany()
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<VideoQuestionOption>().HasOne(x => x.VideoQuestion).WithMany(x => x.OptionList)
            .HasForeignKey(x => x.VideoQuestionId).OnDelete(DeleteBehavior.Cascade);
        // Chương mục của video cũng gắn chặt với bài học như câu hỏi: xóa bài học là chúng hết ý nghĩa.
        b.Entity<VideoChapter>().HasOne(x => x.Lesson).WithMany()
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<VideoQuestionAnswer>().HasOne(x => x.Enrollment).WithMany()
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<VideoQuestionAnswer>().HasOne(x => x.VideoQuestion).WithMany(x => x.AnswerList)
            .HasForeignKey(x => x.VideoQuestionId).OnDelete(DeleteBehavior.Cascade);

        // ---------- CỬA HÀNG ĐIỂM THƯỞNG ----------
        // Restrict ở cả ba khóa ngoại. Lượt đổi là sổ chi tiêu điểm: xóa theo người dùng hay theo sản phẩm
        // là mất luôn bằng chứng vì sao điểm khả dụng của người khác cũng đang được tính như thế. Chặn xóa
        // ở tầng ứng dụng với thông báo nói rõ lý do — xem ShopService.DeleteProduct và
        // UserService.Delete — thay vì để CSDL lặng lẽ dọn dữ liệu.
        b.Entity<ShopRedemption>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        // Người CHỐT đơn cũng là một User: đây là khóa ngoại THỨ HAI trỏ về HBE_USER, mà SqlServer từ chối
        // tạo bảng khi giữa hai bảng có nhiều hơn một đường lan truyền xóa. Cắt nhánh phụ này bằng NoAction.
        b.Entity<ShopRedemption>().HasOne(x => x.HandledBy).WithMany()
            .HasForeignKey(x => x.HandledById).OnDelete(DeleteBehavior.NoAction);
        // Chứng nhận bên ngoài: cùng cách với lượt đổi quà ở trên — người sở hữu Restrict (xóa tài khoản mà còn
        // giấy tờ thì phải dọn có chủ đích, kèm xóa tệp quét trong kho riêng), người duyệt NoAction để SqlServer
        // không thấy hai đường lan truyền xóa cùng đổ về HBE_USER.
        b.Entity<ExternalCertificate>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExternalCertificate>().HasOne(x => x.ReviewedBy).WithMany()
            .HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ShopRedemption>().HasOne(x => x.ShopProduct).WithMany(x => x.RedemptionList)
            .HasForeignKey(x => x.ShopProductId).OnDelete(DeleteBehavior.Restrict);
        // Người tạo sản phẩm rời công ty thì món quà vẫn còn bày bán, chỉ mất tên người tạo.
        b.Entity<ShopProduct>().HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);

        // Xóa một đợt khuyến mãi thì danh sách món đã tick theo đợt đó cũng hết ý nghĩa -> xóa theo.
        b.Entity<ShopPromotionItem>().HasOne(x => x.ShopPromotion).WithMany(x => x.ItemList)
            .HasForeignKey(x => x.ShopPromotionId).OnDelete(DeleteBehavior.Cascade);
        // Nhánh phụ tới sản phẩm để NoAction: từ HBE_SHOP_PRODUCT đã có đường Restrict tới lượt đổi, thêm
        // đường thứ hai qua bảng này là SqlServer từ chối dựng schema ("multiple cascade paths"). Dọn dòng
        // thừa khi xóa sản phẩm do ShopService.DeleteProduct lo.
        b.Entity<ShopPromotionItem>().HasOne(x => x.ShopProduct).WithMany()
            .HasForeignKey(x => x.ShopProductId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ShopPromotion>().HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        // Xóa đợt khuyến mãi KHÔNG được kéo theo lượt đổi đã phát sinh trong đợt đó — đấy là điểm người
        // học đã tiêu thật. Gỡ liên kết và để lại ShopRedemption.PromotionName làm dấu vết.
        b.Entity<ShopRedemption>().HasOne(x => x.ShopPromotion).WithMany()
            .HasForeignKey(x => x.ShopPromotionId).OnDelete(DeleteBehavior.SetNull);

        // Xóa khóa học thì các đoạn tài liệu đã nạp cho nó cũng hết ý nghĩa -> xóa theo.
        b.Entity<DocumentChunk>().HasOne(x => x.Course).WithMany()
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        // Quan hệ với BÀI HỌC phải để NoAction, dù ý muốn là "xóa bài học thì gỡ đoạn tài liệu khỏi bài đó".
        // Lý do: từ Course đã có HAI đường đi tới DOCUMENT_CHUNK — một đường thẳng (Cascade ở trên) và một
        // đường vòng qua Lesson (Course -> Lesson là Cascade, rồi Lesson -> DocumentChunk). SQL Server từ chối
        // hẳn việc tạo bảng khi có hai đường như vậy:
        //     "Introducing FOREIGN KEY constraint ... may cause cycles or multiple cascade paths"
        // Sqlite không kiểm nên trước đây chạy được, nhưng đổi DatabaseProvider sang SqlServer là app chết
        // ngay lúc dựng schema.
        //
        // Hành vi "gỡ khỏi bài học" được giữ nguyên, chỉ chuyển sang làm ở tầng ứng dụng —
        // xem LessonService.Delete().
        b.Entity<DocumentChunk>().HasOne(x => x.Lesson).WithMany()
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
        // Tìm kiếm ngữ nghĩa luôn lọc trước theo khóa học rồi mới tính cosine trong bộ nhớ; nạp lại tài liệu
        // thì xóa theo tên file -> đánh chỉ mục cho cả hai đường truy vấn này.
        b.Entity<DocumentChunk>().HasIndex(x => x.CourseId);
        b.Entity<DocumentChunk>().HasIndex(x => x.SourceFileName);
        // Đồng bộ nội dung khóa học luôn hỏi "bài học này đã nạp chưa, vân tay có đổi không" và màn hình
        // quản trị chỉ liệt kê phần tài liệu tải lên -> cả hai đường đều lọc theo loại nguồn trước tiên.
        b.Entity<DocumentChunk>().HasIndex(x => new { x.SourceKind, x.LessonId });
        b.Entity<DocumentChunk>().HasIndex(x => new { x.SourceKind, x.CourseId });

        // ================= SCORM =================
        // Gói thuộc về khóa học: xóa khóa học thì xóa gói (tệp trên đĩa do CourseService.Delete dọn).
        b.Entity<ScormPackage>().HasOne(x => x.Course).WithMany(x => x.ScormPackageList)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScormPackage>().HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ScormPackage>().HasIndex(x => x.CourseId);
        b.Entity<ScormPackage>().HasIndex(x => x.StorageKey).IsUnique();
        // Bước lộ trình trỏ tới gói: cùng lý do với Lesson/Quiz/ScenarioScene ở trên — từ Course đã có đường
        // Course -> LearningPath -> PathItem, thêm đường Course -> ScormPackage -> PathItem là hai đường. Việc gỡ
        // bước khi xóa gói do ScormService.Delete lo.
        b.Entity<PathItem>().HasOne(x => x.ScormPackage).WithMany(x => x.PathItemList)
            .HasForeignKey(x => x.ScormPackageId).OnDelete(DeleteBehavior.NoAction);
        // Chương (Module) chứa mục con: quan hệ tự tham chiếu. NoAction vì SqlServer không cho lan truyền xóa vòng
        // trên cùng một bảng — xóa chương thì LearningPathService.DeleteItem tự đưa các mục con ra ngoài trước.
        b.Entity<PathItem>().HasOne(x => x.ParentItem).WithMany()
            .HasForeignKey(x => x.ParentItemId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<PathItem>().Property(x => x.CompletionThreshold).HasPrecision(5, 4);
        // Mỗi khóa học chỉ có MỘT gói "của khóa học" — tra theo (khóa học, loại) ở mọi lượt mở trình phát.
        b.Entity<ScormPackage>().HasIndex(x => new { x.CourseId, x.Kind });

        // Bản ghi học gói đi theo lượt học: hủy/xóa lượt học thì xóa theo. Hai khóa ngoại còn lại (người học, gói)
        // để NoAction vì đều có đường thứ hai tới cùng bảng (User -> Enrollment -> đây, Course -> Package /
        // Course -> Enrollment -> đây). Bản XEM THỬ (không có lượt học) do UserService / ScormService /
        // CourseService dọn trước khi xóa người dùng / gói / khóa học.
        b.Entity<ScormRegistration>().HasOne(x => x.Enrollment).WithMany(x => x.ScormRegistrationList)
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScormRegistration>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ScormRegistration>().HasOne(x => x.ScormPackage).WithMany(x => x.RegistrationList)
            .HasForeignKey(x => x.ScormPackageId).OnDelete(DeleteBehavior.NoAction);
        // Một lượt học chỉ có MỘT bản ghi cho mỗi gói — hai bản ghi thì tiến độ chia đôi, học viên học hoài không xong.
        // SqlServer coi mọi NULL là trùng nhau nên phải lọc bỏ bản xem thử (EnrollmentId null).
        var scormEnrollmentIndex = b.Entity<ScormRegistration>().HasIndex(x => new { x.EnrollmentId, x.ScormPackageId }).IsUnique();
        if (Database.IsSqlServer()) scormEnrollmentIndex.HasFilter("[ENROLLMENT_ID] IS NOT NULL");
        b.Entity<ScormRegistration>().HasIndex(x => new { x.UserId, x.ScormPackageId, x.IsPreview });

        b.Entity<ScormRuntime>().HasOne(x => x.ScormRegistration).WithMany(x => x.RuntimeList)
            .HasForeignKey(x => x.ScormRegistrationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ScormRuntime>().HasIndex(x => new { x.ScormRegistrationId, x.ActivityId }).IsUnique();

        b.Entity<ScormRegistration>().Property(x => x.ScoreScaled).HasPrecision(10, 7);
        b.Entity<ScormRegistration>().Property(x => x.BestScoreScaled).HasPrecision(10, 7);
        b.Entity<ScormRuntime>().Property(x => x.ScoreScaled).HasPrecision(10, 7);
        b.Entity<ScormRuntime>().Property(x => x.ProgressMeasure).HasPrecision(10, 7);
        b.Entity<ScormRuntime>().Property(x => x.ScoreRaw).HasPrecision(18, 6);
        b.Entity<ScormRuntime>().Property(x => x.ScoreMin).HasPrecision(18, 6);
        b.Entity<ScormRuntime>().Property(x => x.ScoreMax).HasPrecision(18, 6);

        // Ba cột JSON có thể rất dài (cây hoạt động cả nghìn mục, suspend_data 64.000 ký tự, interactions...).
        // Khai báo rõ kiểu cha PropertyBuilder: ba cột có kiểu string / string? khác nhau nên C# không tự suy ra được
        // một kiểu phần tử chung (PropertyBuilder<string> ≠ PropertyBuilder<string?>), và HasColumnType có sẵn ở kiểu cha.
        foreach (var longText in new PropertyBuilder[]
                 {
                     b.Entity<ScormPackage>().Property(x => x.ActivityTreeJson),
                     b.Entity<ScormRegistration>().Property(x => x.StateJson),
                     b.Entity<ScormRuntime>().Property(x => x.CmiJson),
                 })
        {
            if (Database.ProviderName == "Oracle.EntityFrameworkCore") longText.HasColumnType("NCLOB");
            else if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer") longText.HasColumnType("nvarchar(max)");
        }

        // ================= LỊCH SỬ HỎI ĐÁP VỚI HB MINERVA =================
        // Xóa tài khoản thì xóa luôn lịch sử hỏi đáp của người đó: dữ liệu này chỉ có nghĩa với chính chủ,
        // giữ lại chỉ còn là một đống câu hỏi vô chủ.
        b.Entity<AiChatSession>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AiChatMessage>().HasOne(x => x.AiChatSession).WithMany(x => x.MessageList)
            .HasForeignKey(x => x.AiChatSessionId).OnDelete(DeleteBehavior.Cascade);
        // Danh sách lịch sử của một người luôn sắp theo lượt nói gần nhất.
        b.Entity<AiChatSession>().HasIndex(x => new { x.UserId, x.UpdatedAt });
        b.Entity<AiChatMessage>().HasIndex(x => new { x.AiChatSessionId, x.Id });

        // Mọi câu hỏi của mọi người đều quét qua bảng này trước khi tới model, và luôn quét theo đúng thứ tự
        // xét — đánh chỉ mục để lượt quét đó không phải sắp lại bảng ở mỗi câu hỏi.
        b.Entity<AiQuickReply>().HasIndex(x => new { x.IsActive, x.OrderIndex });

        // ================= DỮ LIỆU GỐC (vai trò) =================
        // IsSystem = true: không cho đổi tên/xóa, vì tên các vai trò này được dùng thẳng trong
        // [Authorize(Roles = ...)] và trong chuỗi duyệt nội dung khóa học. Quyền (FunctionKeys) thì sửa được.
        b.Entity<Role>().HasData(
            NewSystemRole(1, "Admin",           "Quản trị hệ thống"),
            NewSystemRole(2, "Trưởng Nhóm",     "Tạo nội dung bài học và đưa lên Giám Đốc phòng ban"),
            NewSystemRole(3, "Học viên",         "Nhân viên tham gia học"),
            NewSystemRole(4, "Tổng Giám Đốc",    "Quản lý giám đốc, xem báo cáo và bảng xếp hạng toàn công ty"),
            NewSystemRole(5, "Giám Đốc",         "Quản lý theo phòng ban, duyệt nội dung bài học do Trưởng phòng đưa lên"),
            NewSystemRole(6, "Trưởng phòng",     "Duyệt nội dung bài học do Trưởng Nhóm tạo, báo cáo theo phòng ban"),
            NewSystemRole(7, "Giảng Viên",       "Tạo nội dung bài học, xuất bản khóa học mà không cần duyệt")
        );

        // ================= ÁNH XẠ TÊN BẢNG / CỘT KIỂU ORACLE =================
        // Chuyển PascalCase -> UPPER_SNAKE_CASE, tiền tố HBE_ để tránh trùng từ khóa Oracle
        // (đặc biệt USER là từ khóa dành riêng của Oracle).
        foreach (var entity in b.Model.GetEntityTypes())
        {
            entity.SetTableName("HBE_" + ToSnakeUpper(entity.ClrType.Name));

            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeUpper(property.Name));
        }

        var directKeyIndex = b.Entity<Conversation>().HasIndex(x => x.DirectKey).IsUnique();
            if (Database.IsSqlServer()) directKeyIndex.HasFilter("[DIRECT_KEY] IS NOT NULL");

                b.Entity<ConversationMember>().HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
                b.Entity<ChatMessage>().HasIndex(x => new { x.ConversationId, x.Id });

                b.Entity<Conversation>().HasOne(x => x.CreatedByUser).WithMany()
                    .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
                b.Entity<ConversationMember>().HasOne(x => x.User).WithMany()
                    .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
                b.Entity<ChatMessage>().HasOne(x => x.SenderUser).WithMany()
                    .HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.NoAction);
    }

    /// <summary>Một vai trò cài sẵn, kèm bộ quyền mặc định dựng đúng bằng menu mà vai trò đó đang thấy.</summary>
    private static Role NewSystemRole(int id, string name, string description) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        FunctionKeys = FunctionKey.DefaultKeysFor(name),
        IsSystem = true,
        IsActive = true
    };

    /// <summary>Ví dụ: "QrCodeUrl" -> "QR_CODE_URL", "LessonProgress" -> "LESSON_PROGRESS".</summary>
    private static string ToSnakeUpper(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool needUnderscore = i > 0 && char.IsUpper(c) &&
                (!char.IsUpper(name[i - 1]) ||
                 (i + 1 < name.Length && char.IsLower(name[i + 1])));

            if (needUnderscore) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
