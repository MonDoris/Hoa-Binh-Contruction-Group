// File: Infrastructure/Persistence/Seeding/DbSeeder.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Infrastructure.Persistence.Seeding;

/// <summary>Tạo dữ liệu mẫu tối thiểu để đăng nhập và thử nghiệm hệ thống.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // ----- Đồng bộ danh sách Role -----
        // HasData trong AppDbContext chỉ áp dụng khi tạo schema mới (EnsureCreated trên DB rỗng).
        // Với DB dev đã tồn tại từ trước, cần tự đổi tên/thêm role ở đây để không phải xóa dữ liệu.
        var oldTrainerRole = await db.Role.FirstOrDefaultAsync(x => x.Name == "Trainer");
        if (oldTrainerRole != null)
        {
            oldTrainerRole.Name = RoleName.Trainer;
            oldTrainerRole.Description = "Tạo nội dung bài học và đưa lên Giám Đốc phòng ban";
        }

        var oldLearnerRole = await db.Role.FirstOrDefaultAsync(x => x.Name == "Learner");
        if (oldLearnerRole != null)
            oldLearnerRole.Name = RoleName.Learner;

        var missingRoleList = new[]
        {
            new Role { Name = RoleName.ManagingDirector, Description = "Quản lý giám đốc, xem báo cáo và bảng xếp hạng toàn công ty" },
            new Role { Name = RoleName.Director,         Description = "Quản lý theo phòng ban, duyệt nội dung bài học do Trưởng phòng đưa lên" },
            new Role { Name = RoleName.HeadOfDepartment, Description = "Duyệt nội dung bài học do Trưởng Nhóm tạo, báo cáo theo phòng ban" }
        };
        foreach (var role in missingRoleList)
        {
            if (!await db.Role.AnyAsync(x => x.Name == role.Name))
                db.Role.Add(role);
        }
        await db.SaveChangesAsync();

        // ----- Đánh dấu vai trò cài sẵn + nạp quyền mặc định -----
        // Chỉ nạp TOÀN BỘ khi vai trò CHƯA có quyền, để không đè lên phần Admin đã tự chỉnh ở màn hình "Vai trò".
        // Bộ quyền mặc định dựng đúng bằng menu mà vai trò đó vẫn thấy từ trước, nên không đổi hành vi hiện tại.
        //
        // Vai trò ĐÃ có quyền thì chỉ bổ sung khóa của những CHỨC NĂNG HOÀN TOÀN MỚI (xem AddNewModuleKey):
        // thêm một màn hình mới vào hệ thống mà không làm bước này thì mọi cơ sở dữ liệu đang chạy đều
        // không thấy menu mới, vì quyền của vai trò đã lưu từ trước không thể chứa khóa vừa được nghĩ ra.
        var systemRoleNameList = new[]
        {
            RoleName.Admin, RoleName.Trainer, RoleName.Learner,
            RoleName.ManagingDirector, RoleName.Director, RoleName.HeadOfDepartment
        };

        var systemRoleList = await db.Role.Where(x => systemRoleNameList.Contains(x.Name)).ToListAsync();
        foreach (var role in systemRoleList)
        {
            role.IsSystem = true;
            role.IsActive = true;

            // Admin là ngoại lệ: bộ quyền của nó không sửa được ở màn hình "Vai trò" (RoleService luôn ghi
            // đè bằng bộ mặc định), nên ở đây cũng đặt lại đúng bộ mặc định. Chỉ bổ sung như các vai trò khác
            // thì quyền bị GỠ khỏi bộ mặc định — VD course.create/course.update khi Admin thôi soạn nội dung —
            // sẽ nằm lại vĩnh viễn trong database cũ, và không có màn hình nào gỡ chúng ra được.
            if (role.Name == RoleName.Admin)
                role.FunctionKeys = FunctionKey.DefaultKeysFor(RoleName.Admin);
            else if (string.IsNullOrWhiteSpace(role.FunctionKeys))
                role.FunctionKeys = FunctionKey.DefaultKeysFor(role.Name);
            else
                role.FunctionKeys = FunctionKey.AddNewModuleKey(role.FunctionKeys, role.Name);
        }
        await db.SaveChangesAsync();

        // ----- Phòng ban -----
        if (!await db.Department.AnyAsync())
        {
            db.Department.AddRange(
                new Department { Code = "IT",  Name = "Phòng Công nghệ thông tin" },
                new Department { Code = "HR",  Name = "Phòng Nhân sự" },
                new Department { Code = "SAL", Name = "Phòng Kinh doanh" },
                new Department { Code = "ACC", Name = "Phòng Kế toán" }
            );
            await db.SaveChangesAsync();
        }

        // ----- Vị trí công việc -----
        if (!await db.Position.AnyAsync())
        {
            var it = await db.Department.FirstAsync(x => x.Code == "IT");
            var hr = await db.Department.FirstAsync(x => x.Code == "HR");
            var sal = await db.Department.FirstAsync(x => x.Code == "SAL");

            db.Position.AddRange(
                new Position { Code = "DEV",  Name = "Lập trình viên",      DepartmentId = it.Id },
                new Position { Code = "QA",   Name = "Kiểm thử phần mềm",   DepartmentId = it.Id },
                new Position { Code = "HRO",  Name = "Chuyên viên nhân sự", DepartmentId = hr.Id },
                new Position { Code = "SALE", Name = "Nhân viên kinh doanh", DepartmentId = sal.Id }
            );
            await db.SaveChangesAsync();
        }

        // ----- Tài khoản mặc định -----
        if (!await db.User.AnyAsync())
        {
            var it = await db.Department.FirstAsync(x => x.Code == "IT");
            var dev = await db.Position.FirstAsync(x => x.Code == "DEV");

            var adminRole            = await db.Role.FirstAsync(x => x.Name == RoleName.Admin);
            var trainerRole          = await db.Role.FirstAsync(x => x.Name == RoleName.Trainer);
            var learnerRole          = await db.Role.FirstAsync(x => x.Name == RoleName.Learner);
            var headOfDepartmentRole = await db.Role.FirstAsync(x => x.Name == RoleName.HeadOfDepartment);
            var directorRole         = await db.Role.FirstAsync(x => x.Name == RoleName.Director);
            var managingDirectorRole = await db.Role.FirstAsync(x => x.Name == RoleName.ManagingDirector);

            db.User.AddRange(
                new User
                {
                    EmployeeCode = "NV001",
                    FullName = "Quản trị hệ thống",
                    Email = "admin@hbelearning.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                    RoleId = adminRole.Id,
                    DepartmentId = it.Id
                },
                new User
                {
                    EmployeeCode = "NV002",
                    FullName = "Nguyễn Văn Đào Tạo",
                    Email = "trainer@hbelearning.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Trainer@123"),
                    RoleId = trainerRole.Id,
                    DepartmentId = it.Id
                },
                new User
                {
                    EmployeeCode = "NV003",
                    FullName = "Trần Thị Học Viên",
                    Email = "learner@hbelearning.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Learner@123"),
                    RoleId = learnerRole.Id,
                    DepartmentId = it.Id,
                    PositionId = dev.Id
                },
                // Cùng phòng ban IT với Trưởng Nhóm (NV002) để có thể duyệt các khóa học mẫu do người đó tạo.
                new User
                {
                    EmployeeCode = "NV004",
                    FullName = "Lê Thị Trưởng Phòng",
                    Email = "truongphong@hbelearning.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Hbe@12345"),
                    RoleId = headOfDepartmentRole.Id,
                    DepartmentId = it.Id
                },
                new User
                {
                    EmployeeCode = "NV005",
                    FullName = "Phạm Văn Giám Đốc",
                    Email = "giamdoc@hbelearning.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("GiamDoc@123"),
                    RoleId = directorRole.Id,
                    DepartmentId = it.Id
                },
                new User
                {
                    EmployeeCode = "NV006",
                    FullName = "Hoàng Văn Tổng Giám Đốc",
                    Email = "tonggiamdoc@hbelearning.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("TGD@123456789"),
                    RoleId = managingDirectorRole.Id
                }
            );
            await db.SaveChangesAsync();
        }

        await SeedBuiltinSimSceneAsync(db);

        await SeedWeeklyMissionAsync(db);
        await SeedPointRuleAsync(db);
        await SeedAiQuickReplyAsync(db);

        await RemoveDemoSimCourseAsync(db);
    }

    /// <summary>Ba nhiệm vụ tuần mặc định — đúng bộ vốn được viết cứng trong DashboardService trước khi
    /// quản trị viên cấu hình được, nên hệ thống đang chạy nâng cấp lên không thấy ô nhiệm vụ đổi khác.
    ///
    /// Chỉ chạy khi bảng RỖNG, giống các seed danh mục khác. Hệ quả cần biết: xóa sạch nhiệm vụ thì lần khởi
    /// động sau ba nhiệm vụ này quay lại. Muốn tạm ẩn hết thì TẮT chúng (IsActive = false) chứ đừng xóa —
    /// bản ghi vẫn còn nên seeder không xen vào.
    ///
    /// Mức thưởng 5/10/30 cố ý đặt bằng đúng con số vẫn hiện trên ô nhiệm vụ từ trước, để bản nâng cấp
    /// không tự ý hào phóng thêm với ai. Đây chỉ là mức khởi điểm — quản trị viên cân lại tùy chính sách,
    /// và thưởng cho cả một tuần đủ chỉ tiêu thì thường nên cao hơn điểm của một hoạt động lẻ.</summary>
    /// <summary>Gieo bảng điểm hoạt động lần đầu bằng đúng mức vẫn đang chạy từ trước (5/10/30/15), để bản
    /// nâng cấp không tự ý đổi cách tính điểm của bất kỳ hệ thống nào đang vận hành.
    ///
    /// Bổ sung theo TỪNG loại chứ không "bảng rỗng thì gieo cả bốn dòng": thêm một loại hoạt động mới trong
    /// bản sau, hệ thống đang chạy đã có bốn dòng cũ sẽ không bao giờ nhận được dòng thứ năm — và loại mới
    /// đó lặng lẽ cộng 0 điểm. Mức quản trị viên đã tự đặt thì không đụng tới.</summary>
    private static async Task SeedPointRuleAsync(AppDbContext db)
    {
        var existingReasonList = await db.PointRule.Select(x => x.Reason).ToListAsync();
        var missingList = RankingEngine.DefaultPointTable
            .Where(x => !existingReasonList.Contains(x.Key))
            .Select(x => new PointRule
            {
                Reason = x.Key,
                Points = x.Value,
                Threshold = RankingEngine.DefaultThresholdTable.TryGetValue(x.Key, out var threshold) ? threshold : null,
            })
            .ToList();

        if (missingList.Count == 0) return;

        db.PointRule.AddRange(missingList);
        await db.SaveChangesAsync();
    }

    private static async Task SeedWeeklyMissionAsync(AppDbContext db)
    {
        if (await db.WeeklyMission.AnyAsync()) return;

        db.WeeklyMission.AddRange(
            new WeeklyMission { Label = "Học xong 5 bài học", Reason = PointReason.LessonCompleted, Target = 5, RewardPoint = 5, OrderIndex = 1 },
            new WeeklyMission { Label = "Vượt qua 2 bài kiểm tra", Reason = PointReason.QuizPassed, Target = 2, RewardPoint = 10, OrderIndex = 2 },
            new WeeklyMission { Label = "Hoàn thành 1 khóa học", Reason = PointReason.CourseCompleted, Target = 1, RewardPoint = 30, OrderIndex = 3 }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>Mã của hai khóa học demo cho tính năng Mô phỏng 3D mà seeder từng tự tạo ra.
    ///
    /// Chúng là DỮ LIỆU MINH HỌA nhưng lại được phân công thẳng cho một học viên có thật, nên hiện lên trong
    /// màn "Khóa học của tôi" của người đó y như một khóa học được giao thật — và vì không khai báo đối tượng
    /// đào tạo mục tiêu nào, khóa đã xuất bản còn lọt cả vào mục "Khóa học phù hợp với bạn" của MỌI học viên
    /// (xem CourseService.GetMyAvailable: khóa không có CourseTarget được coi là "dành cho tất cả").</summary>
    private static readonly string[] DemoSimCourseCodeList = { "DEMO-SIM-001", "DEMO-SIM-HR-001" };

    /// <summary>Gỡ bỏ hai khóa học demo Mô phỏng 3D khỏi hệ thống.
    ///
    /// Nội dung mô phỏng của chúng đã không còn là thứ duy nhất giữ hai bối cảnh 3D nữa: bối cảnh giờ là mục
    /// chính thức của Thư viện 3D (xem <see cref="SeedBuiltinSimSceneAsync"/>), tồn tại độc lập với mọi khóa
    /// học. Vì vậy xóa khóa demo không làm mất bối cảnh nào.
    ///
    /// CHẠY MỖI LẦN KHỞI ĐỘNG chứ không phải chỉ khi tạo DB mới: DB dev/triển khai đã lỡ sinh ra hai khóa này
    /// từ những lần chạy trước, gỡ mã seed thôi thì chúng vẫn nằm nguyên đó. Không tìm thấy gì thì hàm thoát
    /// ngay, nên lần khởi động thứ hai trở đi gần như không tốn gì.</summary>
    private static async Task RemoveDemoSimCourseAsync(AppDbContext db)
    {
        var courseList = await db.Course
            .Where(x => DemoSimCourseCodeList.Contains(x.Code))
            .ToListAsync();
        if (courseList.Count == 0) return;

        var courseIdList = courseList.Select(x => x.Id).ToList();

        // Thứ tự xóa là bắt buộc, không phải cho gọn. Enrollment -> Course là Restrict (xem AppDbContext), nên
        // còn một bản ghi phân công là không xóa nổi khóa học. Ngược lại, mọi thứ treo dưới Enrollment
        // (tiến độ bài học, lượt làm bài kiểm tra, LƯỢT CHƠI MÔ PHỎNG kèm chi tiết từng câu trả lời, chứng
        // nhận, thông báo) đều Cascade, nên xóa Enrollment là chúng tự đi theo — và phải đi theo TRƯỚC, vì
        // lượt chơi mô phỏng lại trỏ Restrict ngược lên ScenarioScene mà khóa học sắp kéo theo.
        var enrollmentList = await db.Enrollment.Where(x => courseIdList.Contains(x.CourseId)).ToListAsync();
        if (enrollmentList.Count > 0)
        {
            db.Enrollment.RemoveRange(enrollmentList);
            await db.SaveChangesAsync();
        }

        // Lượt chơi mồ côi: về nguyên tắc mọi lượt chơi đều thuộc một Enrollment vừa xóa ở trên (ScenarioService
        // chỉ cho bắt đầu khi mô phỏng thuộc đúng khóa học của phân công đó), nhưng ràng buộc Restrict lên
        // ScenarioScene không tha thứ cho một dòng sót lại — quét thêm một lượt cho chắc.
        var sceneIdList = await db.ScenarioScene
            .Where(x => courseIdList.Contains(x.CourseId))
            .Select(x => x.Id)
            .ToListAsync();
        if (sceneIdList.Count > 0)
        {
            var attemptList = await db.ScenarioAttempt
                .Where(x => sceneIdList.Contains(x.ScenarioSceneId))
                .ToListAsync();
            if (attemptList.Count > 0)
            {
                db.ScenarioAttempt.RemoveRange(attemptList);
                await db.SaveChangesAsync();
            }
        }

        // Phần còn lại (mô phỏng + tình huống + lựa chọn, bài học, bài kiểm tra, lộ trình, đối tượng đào tạo,
        // lịch sử duyệt) đều Cascade từ Course nên xóa theo.
        db.Course.RemoveRange(courseList);
        await db.SaveChangesAsync();
    }

    /// <summary>Đưa các bối cảnh 3D dựng sẵn bằng code vào Thư viện 3D thành mục chính thức.
    ///
    /// Trước đây "công trường xây dựng" và "văn phòng phòng Nhân sự" chỉ là hai key cứng nằm trong
    /// wwwroot/js/sim-registry.js: mở màn hình Thư viện 3D thì thấy trống trơn dù hệ thống đang có sẵn hai
    /// không gian dùng được, và muốn đổi tên hiển thị thì phải sửa mã nguồn rồi build lại. Có bản ghi ở đây
    /// thì chúng đứng ngang hàng với tệp tải lên: xem trước được, đổi tên/mô tả được, đặt được ảnh bìa.
    ///
    /// KHÔNG PHẢI DỮ LIỆU DEMO nên chạy vô điều kiện, không phụ thuộc vào việc có tài khoản mẫu hay khóa học
    /// mẫu nào hay không — đây là vốn có sẵn của hệ thống, hệ thống nào cũng phải có.
    ///
    /// Idempotent theo SCENE_KEY và CHỈ THÊM phần còn thiếu: chạy lại lần sau không được ghi đè tên/mô tả mà
    /// người quản trị đã sửa trong thư viện.</summary>
    private static async Task SeedBuiltinSimSceneAsync(AppDbContext db)
    {
        // SceneKey phải khớp SIM_SCENE_MAP trong wwwroot/js/sim-registry.js và SceneKeyList trong
        // Controllers/ScenarioService.cs — thiếu mã dựng cảnh thì bản ghi ở đây chỉ dẫn tới màn hình đen.
        // "custom" (bối cảnh tự tải lên) KHÔNG có mặt: nó không phải một không gian, mà là cách để người soạn
        // bài dùng chính tệp trong thư viện làm bối cảnh.
        var builtinList = new[]
        {
            new SimBuiltinScene
            {
                SceneKey = "construction",
                Name = "Công trường xây dựng",
                Category = "Bối cảnh dựng sẵn",
                Description = "Cảnh ngoài trời: nhà đang xây, giàn giáo, cẩu tháp, bãi vật liệu, đường xe chạy. "
                            + "Hợp với kịch bản kiểm tra hiện trường (an toàn lao động, chất lượng thi công) — "
                            + "học viên đi quanh công trường tự tìm điểm nguy cơ, thứ tự xử lý không quan trọng.",
                OrderIndex = 1
            },
            new SimBuiltinScene
            {
                SceneKey = "hr-office",
                Name = "Văn phòng phòng Nhân sự",
                Category = "Bối cảnh dựng sẵn",
                Description = "Cảnh trong nhà: quầy lễ tân, bàn tuyển dụng, phòng phỏng vấn, phòng họp, tủ hồ sơ, "
                            + "khu onboarding. Hợp với kịch bản QUY TRÌNH (tuyển dụng, tiếp nhận nhân viên mới, "
                            + "xử lý hồ sơ) — các bước phải làm theo đúng thứ tự.",
                OrderIndex = 2
            },
        };

        var existingKeyList = await db.SimBuiltinScene.Select(x => x.SceneKey).ToListAsync();
        var missingList = builtinList.Where(x => !existingKeyList.Contains(x.SceneKey)).ToList();
        if (missingList.Count == 0) return;

        db.SimBuiltinScene.AddRange(missingList);
        await db.SaveChangesAsync();
    }

    /// <summary>Bộ câu trả lời cài sẵn mặc định của Pico B — chỉ nạp khi bảng còn TRỐNG.
    ///
    /// Chỉ khi trống là điều kiện bắt buộc: Admin xóa bớt dòng nào là có ý xóa, mà nạp bù ở mỗi lần khởi
    /// động thì dòng họ vừa xóa lại mọc lên sau khi khởi động lại máy chủ và không ai hiểu vì sao.
    ///
    /// Đây là những câu KHÔNG nằm trong tài liệu nội bộ nào — xem <see cref="AiQuickReply"/>.</summary>
    private static async Task SeedAiQuickReplyAsync(AppDbContext db)
    {
        if (await db.AiQuickReply.AnyAsync()) return;

        db.AiQuickReply.AddRange(
            new AiQuickReply
            {
                OrderIndex = 1,
                MatchType = QuickReplyMatchType.Exact,
                Keyword = "hi|hello|hey|alo|chào|chào bạn|xin chào|chào pico|hi pico",
                Answer = "Chào {ten_ngan}! Mình là Pico B, trợ lý đào tạo nội bộ.\n"
                       + "Bạn cần mình giúp gì? Bạn có thể hỏi mình về nội dung các khóa học bạn đang học "
                       + "hoặc về quy định, quy trình trong tài liệu nội bộ của công ty."
            },
            new AiQuickReply
            {
                OrderIndex = 2,
                MatchType = QuickReplyMatchType.Exact,
                Keyword = "cảm ơn|cám ơn|thanks|thank you|tks|cảm ơn bạn|cảm ơn nhé",
                Answer = "Không có gì {ten_ngan} nhé! Còn chỗ nào trong bài học chưa rõ thì cứ hỏi mình."
            },
            new AiQuickReply
            {
                OrderIndex = 3,
                MatchType = QuickReplyMatchType.Exact,
                Keyword = "tạm biệt|bye|goodbye|bai|chào tạm biệt",
                Answer = "Tạm biệt {ten_ngan}! Chúc bạn học tốt, khi nào cần thì quay lại hỏi mình nhé."
            },
            new AiQuickReply
            {
                OrderIndex = 4,
                MatchType = QuickReplyMatchType.Exact,
                Keyword = "bạn là ai|bạn tên gì|pico b là ai|pico b là gì|who are you|bạn là gì",
                Answer = "Mình là Pico B — trợ lý đào tạo nội bộ của công ty.\n"
                       + "Mình trả lời dựa trên tài liệu nội bộ và nội dung các khóa học bạn được phân công. "
                       + "Chỗ nào tài liệu không nói thì mình nói thẳng là chưa có, không tự đoán."
            },
            new AiQuickReply
            {
                OrderIndex = 5,
                MatchType = QuickReplyMatchType.Exact,
                Keyword = "bạn làm được gì|bạn giúp được gì|help|bạn có thể làm gì|hướng dẫn",
                Answer = "Mình giúp được {ten_ngan} mấy việc sau:\n"
                       + "- Giải thích nội dung bài học trong các khóa bạn đang học\n"
                       + "- Tóm tắt một khóa học hoặc một bài cụ thể\n"
                       + "- Tra quy định, quy trình trong tài liệu nội bộ\n"
                       + "Bạn cứ hỏi bằng câu bình thường, càng rõ chủ đề thì mình trả lời càng đúng."
            });

        await db.SaveChangesAsync();
    }

}
