// File: Domain/Enums/AppEnum.cs
namespace HbELearning.Domain.Enums;

/// <summary>
/// Trạng thái vòng đời của khóa học, gồm chuỗi duyệt nội dung nhiều cấp:
/// Draft (Trưởng Nhóm soạn) -> PendingHeadApproval (Trưởng phòng duyệt) ->
/// PendingDirectorApproval (Giám Đốc phòng ban duyệt) -> PendingHrApproval (Giám Đốc Nhân sự duyệt) -> Published.
/// Giá trị Draft/Published/Archived giữ nguyên số cũ để không phá dữ liệu đã lưu.
/// </summary>
public enum CourseStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
    PendingHeadApproval = 3,
    PendingDirectorApproval = 4,
    PendingHrApproval = 5,
    Rejected = 6,

    /// <summary>Đang chờ duyệt ở một bước của LUỒNG CẤP DUYỆT do Admin định nghĩa (Course.ApprovalWorkflowId).
    /// Bước cụ thể nằm ở Course.CurrentStepOrder — trạng thái này cố ý không gắn với một cấp nào, vì số bước
    /// và cấp duyệt của mỗi luồng do Admin tự đặt. Khóa học không chọn luồng vẫn dùng ba trạng thái
    /// PendingHeadApproval / PendingDirectorApproval / PendingHrApproval như cũ.</summary>
    PendingApproval = 7
}

/// <summary>Nhãn tiếng Việt cho CourseStatus, dùng khi ghép vào thông báo trả về cho người dùng.</summary>
public static class CourseStatusExtensions
{
    public static string ToVietnameseLabel(this CourseStatus status) => status switch
    {
        CourseStatus.Draft => "Bản nháp",
        CourseStatus.Published => "Đã xuất bản",
        CourseStatus.Archived => "Lưu trữ",
        CourseStatus.PendingHeadApproval => "Chờ Trưởng phòng duyệt",
        CourseStatus.PendingDirectorApproval => "Chờ Giám Đốc duyệt",
        CourseStatus.PendingHrApproval => "Chờ Giám Đốc Nhân sự duyệt",
        CourseStatus.Rejected => "Từ chối",
        CourseStatus.PendingApproval => "Chờ duyệt",
        _ => status.ToString()
    };
}

/// <summary>
/// Cách xác định NGƯỜI duyệt của một bước trong luồng cấp duyệt (xem <see cref="ApprovalWorkflowStep"/>).
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — chúng nằm trong HBE_APPROVAL_WORKFLOW_STEP.APPROVER_MODE. AnyOfRole = 0 để
/// mọi bước đã cấu hình từ trước (cột thêm sau, mặc định 0) giữ nguyên hành vi cũ.
/// </summary>
public enum StepApproverMode
{
    /// <summary>Gửi cho MỌI người giữ vai trò của bước trong phạm vi phòng ban của bước — ai bấm trước cũng được.</summary>
    AnyOfRole = 0,

    /// <summary>KHÔNG CÒN DÙNG — trước đây là "người soạn chỉ định một người khi Gửi duyệt". Người soạn không
    /// còn được chọn người duyệt cho bài của mình nữa; việc đó thuộc về người dựng luồng (FixedUser).
    /// Giữ lại giá trị để đọc được dữ liệu cũ: server quy các bước còn PickOne về AnyOfRole khi lưu luồng,
    /// và giao diện không còn chào chế độ này.</summary>
    PickOne = 1,

    /// <summary>Người dựng luồng ghim sẵn một người ngay trong định nghĩa luồng (ApprovalWorkflowStep.FixedUserId)
    /// — mọi khóa học chạy luồng này đều do đúng người đó duyệt bước này.</summary>
    FixedUser = 2
}

/// <summary>Loại nội dung của một bài học.</summary>
public enum LessonContentType { Text = 0, Document = 1, Video = 2, Image = 3, Link = 4 }

/// <summary>Học viên được tua video bài học tới đâu. Chỉ có ý nghĩa với bài học dạng Video.</summary>
public enum VideoSeekPolicy
{
    /// <summary>Tua tự do — chỉ bị chặn tại mốc câu hỏi có bật "chặn tua" (hành vi mặc định từ trước).</summary>
    Free = 0,

    /// <summary>Bắt buộc xem hết: không tua vượt quá đoạn xa nhất đã thực sự xem. Tua lùi để xem lại vẫn được.</summary>
    WatchFull = 1,

    /// <summary>Cho bấm thẳng tới mốc câu hỏi trong danh sách dưới bài học — dành cho bài học mà người soạn
    /// chấp nhận học viên chỉ làm câu hỏi, không xem hết video.</summary>
    JumpToQuestion = 2
}

/// <summary>Loại phần tử trong lộ trình học tập.</summary>
/// <summary>Loại một mục trong cấu trúc khóa học (= &lt;item&gt; của SCORM Organization). Module là CỤM (cluster) —
/// một chương gom các mục con, tự nó không có nội dung để phát. Các loại còn lại là hoạt động LÁ, mỗi loại trỏ
/// tới một tài nguyên (SCO) cụ thể.</summary>
public enum PathItemType { Lesson = 0, Quiz = 1, Simulation = 2, Scorm = 3, Module = 4 }

/// <summary>Gói SCORM nhập từ ngoài, hay "gói" của chính một khóa học soạn trên hệ thống (cây hoạt động sinh ra
/// từ cấu trúc khóa học). GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_SCORM_PACKAGE.KIND.</summary>
public enum ScormPackageKind
{
    /// <summary>Gói .zip người soạn tải lên — tệp nằm trong kho SCORM, cây đọc từ imsmanifest.xml.</summary>
    Imported = 0,

    /// <summary>Gói của khóa học: mỗi khóa học có đúng một, cây hoạt động dựng từ cấu trúc khóa học (lộ trình +
    /// chương + mục), mỗi bài học / bài kiểm tra / mô phỏng là một SCO do hệ thống phục vụ.</summary>
    Course = 1
}

/// <summary>Phiên bản chuẩn SCORM của một gói nội dung. Quyết định trình phát dựng bộ API nào cho nội dung tìm
/// thấy: <c>API</c> (1.2) hay <c>API_1484_11</c> (2004). GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_SCORM_PACKAGE.VERSION.</summary>
public enum ScormVersion
{
    Scorm12 = 0,

    /// <summary>SCORM 2004, mọi bản (2nd / 3rd / 4th Edition) — tên bản cụ thể nằm ở ScormPackage.SchemaVersion.</summary>
    Scorm2004 = 1
}

/// <summary>Trạng thái phân công khóa học của người học.</summary>
public enum EnrollmentStatus { Assigned = 0, InProgress = 1, Completed = 2, Failed = 3, Cancelled = 4 }

/// <summary>Trạng thái học của từng bài học.</summary>
public enum ProgressStatus { NotStarted = 0, InProgress = 1, Completed = 2 }

/// <summary>Kiểu câu hỏi trong bài kiểm tra.</summary>
public enum QuestionType { SingleChoice = 0, MultipleChoice = 1, TrueFalse = 2 }

/// <summary>Loại thông báo / nhắc nhở học tập gửi cho người học, gồm cả các bước trong chuỗi duyệt nội dung.</summary>
public enum NotificationType
{
    CourseAssigned = 0, NotStarted = 1, DueSoon = 2, Overdue = 3, RankUp = 4,
    CourseSubmittedForApproval = 5, CourseApproved = 6, CourseRejected = 7, CoursePublished = 8,
    WeeklyMissionDone = 9,

    /// <summary>Nhắc nhở định kỳ mỗi tuần: điểm lại số bài học còn dang dở. Khác NotStarted/DueSoon ở chỗ
    /// nó lặp lại theo lịch (7 ngày một lần) chứ không phải bắn một lần cho mỗi bản ghi phân công.</summary>
    WeeklyReminder = 10,

    /// <summary>Diễn biến của một lượt đổi quà ở Cửa hàng: vừa đổi thành công, được trao quà, hoặc bị hủy
    /// và hoàn điểm. Gộp chung một loại vì cả ba đều dẫn về cùng một chỗ (trang Cửa hàng → Lượt đổi của tôi),
    /// mà nội dung cụ thể đã nằm trong tiêu đề/nội dung của từng thông báo.</summary>
    ShopRedemption = 11,

    /// <summary>Nhắc nhở do CHÍNH người học tự hẹn giờ trên Lịch học (xem <see cref="StudyReminder"/>).
    /// Khác mọi loại trên ở chỗ giờ bắn do người học đặt, không do hệ thống suy ra từ hạn hoàn thành.</summary>
    StudyReminder = 12,

    /// <summary>Diễn biến của một chứng nhận BÊN NGOÀI (xem <see cref="ExternalCertificate"/>): báo nhân viên giấy
    /// của mình đã được AI / Admin xác nhận, bị từ chối hoặc bị thu hồi. (Trước khi AI đối chiếu thay Admin, loại
    /// này còn dùng để báo Admin có giấy mới chờ duyệt — thông báo cũ đó vẫn dẫn về đúng thẻ của Admin.)</summary>
    ExternalCertificate = 13,

    /// <summary>Học viên có phản hồi / bình luận / đánh giá mới về khóa học — thông báo cho chủ khóa học.</summary>
    CourseFeedback = 14
}

/// <summary>Trạng thái của một chứng nhận bên ngoài do nhân viên tự tải lên.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — chúng nằm trong HBE_EXTERNAL_CERTIFICATE.STATUS của mọi giấy đã gửi.</summary>
public enum ExternalCertificateStatus
{
    /// <summary>Vừa gửi lên, AI chưa đối chiếu xong (đang xếp hàng, hoặc máy chủ AI đang tắt).</summary>
    Pending = 0,

    /// <summary>Thông tin trên giấy khớp với thông tin khai và họ tên tài khoản (AI xác nhận, hoặc Admin xác
    /// nhận tay) — chỉ lúc này mới vào danh sách chứng nhận in ra.</summary>
    Approved = 1,

    /// <summary>Không khớp / không đọc được, kèm lý do. Nhân viên xóa đi rồi gửi lại bản đúng.</summary>
    Rejected = 2
}

/// <summary>Chu kỳ lặp của một lời nhắc học tập người dùng tự đặt.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — chúng nằm trong HBE_STUDY_REMINDER.REPEAT của mọi lời nhắc đã đặt.</summary>
public enum ReminderRepeat
{
    /// <summary>Chỉ báo đúng một lần rồi tự tắt.</summary>
    None = 0,
    Daily = 1,

    /// <summary>Thứ Hai đến thứ Sáu — dành cho người chỉ học trong giờ làm việc.</summary>
    Weekdays = 2,

    /// <summary>Mỗi tuần một lần, đúng thứ và đúng giờ của lần hẹn đầu tiên.</summary>
    Weekly = 3
}

/// <summary>Lý do được cộng điểm xếp hạng (Ranking).
///
/// Mọi lý do TRỪ WeeklyMissionCompleted đều có một mức điểm do quản trị viên đặt trong bảng HBE_POINT_RULE
/// (xem màn hình Xếp hạng → Điểm cho mỗi hoạt động). Riêng WeeklyMissionCompleted không có mức chung: số
/// điểm do quản trị viên đặt cho từng nhiệm vụ (xem WeeklyMission.RewardPoint), nên đừng tra nó ở đó.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — chúng nằm trong HBE_USER_POINT_LOG.REASON của mọi dòng điểm đã cộng và trong
/// HBE_WEEKLY_MISSION.REASON. Đánh số lại là toàn bộ lịch sử điểm đổi nghĩa. Thêm loại mới thì nối tiếp số
/// cuối, đừng chèn vào giữa.</summary>
public enum PointReason
{
    LessonCompleted = 0, QuizPassed = 1, CourseCompleted = 2, SimulationCompleted = 3,
    WeeklyMissionCompleted = 4,

    /// <summary>Mở hệ thống trong ngày — cộng MỘT lần cho lần đăng nhập đầu tiên mỗi ngày.</summary>
    DailyLogin = 5,

    /// <summary>Học đủ số phút quy định trong một ngày. Ngưỡng phút do quản trị viên đặt
    /// (PointRule.Threshold), không phải con số cố định trong mã.</summary>
    DailyStudyTime = 6,

    /// <summary>Trả lời đúng một câu hỏi chèn giữa video. Mỗi câu chỉ cộng một lần.</summary>
    VideoQuestionPassed = 7
}

/// <summary>Trạng thái một lượt chơi mô phỏng 3D.</summary>
public enum ScenarioAttemptStatus { InProgress = 0, Completed = 1 }

/// <summary>Trạng thái vòng đời của một phòng học online.</summary>
public enum OnlineClassRoomStatus { Scheduled = 0, Live = 1, Ended = 2, Cancelled = 3 }

/// <summary>Loại sự kiện trong nhật ký diễn biến (biên bản) của một buổi học online.</summary>
public enum OnlineClassRoomEventType
{
    Started = 0, Ended = 1, Joined = 2, Left = 3, ChatMessage = 4,
    MicOn = 5, MicOff = 6, CamOn = 7, CamOff = 8,
    // Ghi hình của Chủ trì. Giá trị số lưu trong từng dòng Diễn biến: chỉ được thêm vào CUỐI, không bao giờ đánh số lại.
    RecordingStarted = 9, RecordingPaused = 10, RecordingResumed = 11, RecordingStopped = 12
}

/// <summary>Trạng thái một lượt đổi quà ở Cửa hàng điểm thưởng.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — chúng nằm trong HBE_SHOP_REDEMPTION.STATUS của mọi lượt đổi đã ghi.
///
/// Điểm bị trừ ngay lúc đổi (Pending), không đợi tới lúc trao quà: nếu chỉ trừ khi trao thì một người có
/// 100 điểm đổi được vô số món trong lúc chờ, rồi quản trị viên mới phát hiện ra là không đủ điểm. Vì vậy
/// <see cref="Cancelled"/> luôn HOÀN lại đúng số điểm đã trừ và trả số lượng về kho.</summary>
public enum ShopRedemptionStatus
{
    /// <summary>Đã trừ điểm, đang chờ quản trị viên trao quà.</summary>
    Pending = 0,

    /// <summary>Đã trao quà cho người đổi — trạng thái kết thúc, không hoàn điểm.</summary>
    Delivered = 1,

    /// <summary>Đã hủy: điểm được hoàn lại cho người đổi và số lượng trả về kho.</summary>
    Cancelled = 2
}

/// <summary>Cách một chương trình khuyến mãi của cửa hàng làm rẻ đi giá điểm.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_SHOP_PROMOTION.DISCOUNT_TYPE.</summary>
public enum ShopDiscountType
{
    /// <summary>Giảm theo phần trăm giá gốc (DiscountValue = 1..100).</summary>
    Percent = 0,

    /// <summary>Trừ thẳng một số điểm cố định khỏi giá gốc (DiscountValue = số điểm).</summary>
    FixedPoint = 1
}

/// <summary>Chương trình khuyến mãi áp cho những sản phẩm nào.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_SHOP_PROMOTION.SCOPE_TYPE.</summary>
public enum ShopPromotionScope
{
    /// <summary>Mọi món đang bày bán. Sản phẩm thêm mới GIỮA đợt cũng được giảm ngay, không phải sửa lại
    /// chương trình — đây là điểm khác thật sự so với việc tick tay từng món.</summary>
    AllProduct = 0,

    /// <summary>Mọi món thuộc một nhóm (ShopProduct.Category), cũng tự bắt các món thêm mới vào nhóm đó.</summary>
    Category = 1,

    /// <summary>Đúng những món admin tick, ghi ở bảng HBE_SHOP_PROMOTION_ITEM.</summary>
    SelectedProduct = 2
}

public enum ConversationType
{
    Direct = 0,
    Group = 1
}

/// <summary>Kiểu nội dung của một tin nhắn trong hộp chat nội bộ (<see cref="ChatMessage"/>).
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong cột HBE_CHAT_MESSAGE.MESSAGE_TYPE. 0 là mặc định để mọi tin nhắn
/// đã lưu trước khi có cột này tự động được hiểu là tin chữ, đúng như bản chất của chúng.
///
/// Giao diện vẽ mỗi kiểu một cách khác hẳn nhau (bong bóng chữ / ảnh xem được / thẻ tệp tải về / nhãn
/// dán to / trình phát tiếng), nên phải LƯU kiểu chứ không đoán lại từ đuôi tệp lúc hiển thị: đuôi tệp
/// không phân biệt nổi ảnh gửi như ảnh với ảnh gửi như tệp đính kèm, và một đoạn ghi âm .webm thì trông
/// y hệt một video .webm.</summary>
public enum ChatMessageType
{
    /// <summary>Tin chữ thuần.</summary>
    Text = 0,

    /// <summary>Ảnh — hiện thẳng trong khung chat, bấm vào mở cỡ lớn.</summary>
    Image = 1,

    /// <summary>Tệp đính kèm bất kỳ (kể cả từng tệp con của một thư mục) — hiện thành thẻ có nút tải về.</summary>
    File = 2,

    /// <summary>Nhãn dán: nội dung là một ký tự emoji, không kèm tệp nào.</summary>
    Sticker = 3,

    /// <summary>Đoạn ghi âm thu ngay trên trình duyệt — hiện thành trình phát tiếng.</summary>
    Audio = 4
}
/// <summary>Một đoạn trong kho tri thức AI (<see cref="DocumentChunk"/>) từ đâu mà có.
///
/// Có cột này thì hai nguồn nuôi kho tri thức mới sống chung được trong một bảng mà không giẫm chân nhau:
/// tài liệu Admin tải lên do CON NGƯỜI quản lý (thêm/xóa bằng tay ở màn hình "Tài liệu AI"), còn nội dung
/// khóa học do MÁY đồng bộ lại mỗi khi bài học đổi. Không phân biệt được thì lượt đồng bộ tự động sẽ xóa
/// nhầm tài liệu người ta vừa tải lên, và ngược lại màn hình quản trị sẽ liệt kê hàng trăm "tài liệu"
/// thực chất là bài học.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_DOCUMENT_CHUNK.SOURCE_KIND. 0 là mặc định để mọi đoạn đã nạp
/// từ trước (đều là tài liệu tải lên) giữ nguyên ý nghĩa cũ mà không phải vá dữ liệu.</summary>
public enum KnowledgeSourceKind
{
    /// <summary>Tài liệu PDF/Word/TXT Admin tải lên ở màn hình "Tài liệu AI".</summary>
    UploadedDocument = 0,

    /// <summary>Nội dung một BÀI HỌC, do dịch vụ đồng bộ tự nạp (xem <see cref="Services.ICourseKnowledgeService"/>).</summary>
    CourseLesson = 1,

    /// <summary>Phần giới thiệu chung của một KHÓA HỌC (tên, mô tả, danh sách bài học) — để AI trả lời được
    /// những câu hỏi về tổng thể khóa học chứ không chỉ về chi tiết trong từng bài.</summary>
    CourseOverview = 2
}

/// <summary>Ai là người nói trong một lượt hội thoại với HB Minerva.
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_AI_CHAT_MESSAGE.ROLE.</summary>
public enum AiChatRole
{
    /// <summary>Câu hỏi của người dùng.</summary>
    User = 0,

    /// <summary>Câu trả lời của HB Minerva.</summary>
    Assistant = 1
}

/// <summary>Câu hỏi phải giống câu cài sẵn tới mức nào thì được coi là khớp (xem <see cref="AiQuickReply"/>).
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_AI_QUICK_REPLY.MATCH_TYPE.</summary>
public enum QuickReplyMatchType
{
    /// <summary>Khớp TRỌN câu hỏi (sau khi bỏ hoa thường, bỏ dấu và bỏ dấu câu cuối câu).
    /// Đây là mặc định vì nó an toàn: "hi" chỉ bắt đúng khi người ta gõ mỗi chữ "hi".</summary>
    Exact = 0,

    /// <summary>Câu hỏi có CHỨA từ khóa, tính theo RANH GIỚI TỪ chứ không phải chuỗi con.
    ///
    /// Ranh giới từ là phần bắt buộc: so chuỗi con thì từ khóa "hi" khớp luôn cả "lịch sử thi công" hay
    /// "quy trình nghiệm thu", và người hỏi nhận được lời chào thay cho câu trả lời họ cần.</summary>
    Contains = 1
}

/// <summary>Loại phản hồi của học viên về một khóa học.
///
/// GIÁ TRỊ SỐ LÀ VĨNH VIỄN — nằm trong HBE_COURSE_FEEDBACK.KIND.</summary>
public enum FeedbackKind
{
    /// <summary>Bình luận về một vấn đề gặp phải trong quá trình học. Có thể có các trả lời từ Giảng Viên hoặc chủ khóa.</summary>
    Comment = 0,

    /// <summary>Đánh giá sao (1–5) cho khóa học sau khi hoàn thành. Mỗi học viên chỉ đánh giá một lần per enrollment, nhưng có thể sửa được.</summary>
    Review = 1
}
