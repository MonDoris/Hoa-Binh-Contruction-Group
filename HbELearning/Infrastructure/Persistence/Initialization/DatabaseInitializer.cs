// File: Infrastructure/Persistence/Initialization/DatabaseInitializer.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Infrastructure.Persistence.Initialization;

/// <summary>
/// Dựng và vá lược đồ database lúc khởi động, rồi nạp dữ liệu nền.
///
/// Trước khi tách lớp, toàn bộ phần này nằm ngay trong Program.cs và chiếm khoảng 360 trên 514 dòng của
/// tệp đó — tức là điểm khởi động của ứng dụng chủ yếu đang làm việc của database. Nó thuộc về
/// Infrastructure: mọi thứ ở đây là chi tiết của EF Core và của cú pháp DDL riêng từng provider.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>Gọi một lần lúc khởi động, trước khi đường ống middleware bắt đầu nhận yêu cầu.</summary>
    public static async Task InitializeAsync(IServiceProvider rootServiceProvider, IWebHostEnvironment environment)
    {
        using var scope = rootServiceProvider.CreateScope();
        var dbProvider = scope.ServiceProvider.GetRequiredService<DatabaseProviderName>().Value;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        // Oracle dùng Migration (có script SQL để nộp/triển khai).
        // Sqlite/SqlServer chỉ dùng cho phát triển nên tạo thẳng schema từ model.
        if (dbProvider == DbProviderSetup.Oracle)
            await db.Database.MigrateAsync();
        else
        {
            await db.Database.EnsureCreatedAsync();
            // EnsureCreated chỉ tạo schema khi db CHƯA tồn tại — db đã có sẵn từ trước mà model vừa thêm bảng
            // mới (VD Notification) thì bảng đó sẽ không tự xuất hiện. Bổ sung thủ công để không phải xóa db
            // (mất dữ liệu) mỗi khi thêm bảng mới trong lúc phát triển.
            //
            // Chạy cho cả Sqlite lẫn SqlServer. Trước đây chỉ Sqlite được vá, nên mọi bảng thêm mới đều lặng lẽ
            // vắng mặt trên SqlServer cho tới khi ai đó xóa cả database đi tạo lại — mà đó chính là provider
            // đang chạy thật (xem "DatabaseProvider" trong appsettings.json).
            await EnsureNewTableAsync(db, dbProvider);
            if (dbProvider == DbProviderSetup.Sqlite) await EnsureNewColumnAsync(db);
            else if (dbProvider == DbProviderSetup.SqlServer) await EnsureNewColumnSqlServerAsync(db);
        }

        var isDevelopment = environment.IsDevelopment();
        await DbSeeder.SeedAsync(db, isDevelopment);

        // Chạy thật mà database chưa có ai: nói rõ ra thay vì im lặng dựng sẵn một tài khoản Admin có mật
        // khẩu nằm trong mã nguồn (xem DbSeeder.SeedAsync).
        if (!isDevelopment && !await db.User.AnyAsync())
            logger.LogWarning(
                "Database chua co tai khoan nao. He thong KHONG tu tao tai khoan demo khi chay that — "
                + "hay tao tai khoan quan tri dau tien bang tay va dat mat khau rieng.");

        // Nạp wwwroot/locales/*.json vào bảng dịch. Chạy mọi lần khởi động để nhận khóa mới của bản cập nhật,
        // nhưng KHÔNG đè lên câu chữ công ty đã sửa ở màn hình "Dịch ngôn ngữ" — xem TranslationSeeder.
        var webRootPath = environment.WebRootPath;
        if (string.IsNullOrEmpty(webRootPath))
            webRootPath = Path.Combine(environment.ContentRootPath, "wwwroot");
        await TranslationSeeder.SeedAsync(db, webRootPath, logger);

        logger.LogInformation("Database provider dang su dung: {Provider}", dbProvider);
    }

    /// <summary>Tạo các bảng còn thiếu so với model hiện tại trên một db Sqlite đã tồn tại từ trước (không đụng tới bảng đã có).</summary>
    private static async Task EnsureNewTableAsync(AppDbContext db, string dbProvider)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // Câu hỏi "bảng này có chưa" mỗi provider trả lời một kiểu; phần còn lại của hàm dùng chung được vì
        // GenerateCreateScript() vốn đã sinh đúng cú pháp DDL của provider đang chạy.
        var tableExistsSql = dbProvider == DbProviderSetup.SqlServer
            ? "SELECT name FROM sys.tables WHERE name = @name"
            : "SELECT name FROM sqlite_master WHERE type='table' AND name = @name";

        var statementList = db.Database.GenerateCreateScript()
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            // SqlServer chèn thêm dòng phân lô "GO" — để nguyên trong câu lệnh là lỗi cú pháp khi chạy trực tiếp.
            .Select(s => string.Join('\n', s.Split('\n').Where(line => line.Trim() != "GO")).Trim())
            .Where(s => s.Length > 0)
            .ToList();

        // Bảng vừa tạo trong lần chạy này — dùng để biết index nào cần tạo theo (xem vòng lặp bên dưới).
        var createdTableList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var statement in statementList.Where(s => s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)))
        {
            // Sqlite bọc tên bảng trong "dấu nháy kép", SqlServer bọc trong [ngoặc vuông] — nhận cả hai kiểu.
            var match = System.Text.RegularExpressions.Regex.Match(statement, "CREATE TABLE [\"\\[]?(\\w+)");
            if (!match.Success) continue;

            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = tableExistsSql;
            var parameter = checkCommand.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = match.Groups[1].Value;
            checkCommand.Parameters.Add(parameter);

            if (await checkCommand.ExecuteScalarAsync() is not null) continue; // bảng đã tồn tại

            using var createCommand = connection.CreateCommand();
            createCommand.CommandText = statement;
            await createCommand.ExecuteNonQueryAsync();
            createdTableList.Add(match.Groups[1].Value);
        }

        if (createdTableList.Count == 0) return;

        // Index của bảng mới nằm ở những câu lệnh RIÊNG trong script, không nằm trong câu CREATE TABLE — bỏ qua
        // chúng thì bảng mới ra đời mà thiếu cả ràng buộc UNIQUE, và dữ liệu trùng chỉ lộ ra rất lâu về sau.
        // Chỉ tạo index cho bảng vừa dựng ở trên: bảng đã có từ trước có thể đang mang dữ liệu vi phạm một index
        // mới thêm vào model, tạo ép ở đây sẽ làm ứng dụng không khởi động nổi.
        foreach (var statement in statementList.Where(s => s.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)
                                                        && s.Contains(" INDEX ", StringComparison.OrdinalIgnoreCase)))
        {
            var match = System.Text.RegularExpressions.Regex.Match(statement, " ON [\"\\[]?(\\w+)");
            if (!match.Success || !createdTableList.Contains(match.Groups[1].Value)) continue;

            using var indexCommand = connection.CreateCommand();
            indexCommand.CommandText = statement;
            await indexCommand.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Thêm các cột còn thiếu trên những bảng đã tồn tại từ trước của một db SqlServer.
    ///
    /// Danh sách này KHÔNG chép lại toàn bộ các cột mà bản Sqlite đang vá: database SqlServer hiện hành được
    /// dựng từ model ở thời điểm gần đây nên đã có sẵn chúng. Chỉ liệt kê những cột thêm từ lúc cơ chế vá này
    /// bắt đầu chạy cho SqlServer trở đi — chép thừa vào đây không sai, nhưng mỗi dòng thừa là một dòng phải
    /// đọc lại mỗi lần có người dò xem cột nào đã được vá.</summary>
    private static async Task EnsureNewColumnSqlServerAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // Ngưỡng của hoạt động cần đo lường (số phút học tối thiểu trong ngày) — xem PointRule.Threshold.
        // Bảng điểm hoạt động có thể đã được tạo từ bản trước, lúc đó chưa có cột này.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_POINT_RULE", ("THRESHOLD", "int NULL"));
        // Cỡ người trong cảnh 3D — mỗi cảnh một hệ số, xem ScenarioScene.CharacterScale.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_SCENARIO_SCENE",
            ("CHARACTER_SCALE", "real NOT NULL DEFAULT 1"));
        // Tình huống gắn vào một nhân vật thay vì một điểm cố định — xem ScenarioNode.AttachedCharacterId.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_SCENARIO_NODE",
            ("ATTACHED_CHARACTER_ID", "int NULL"));
        // Màu trang phục của từng nhân vật — xem ScenarioCharacter.OutfitColor.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_SCENARIO_CHARACTER",
            ("OUTFIT_COLOR", "nvarchar(20) NULL"));
        // Vai trò cha để kế thừa quyền và danh tính (VD "Trưởng phòng IT" kế thừa "Trưởng phòng").
        // Cố ý KHÔNG kèm khóa ngoại: cột được thêm vào một bảng đã có dữ liệu, mà ràng buộc khóa ngoại thêm sau
        // sẽ chặn cả những dòng cũ hợp lệ nếu có bản ghi rác. Tính đúng đắn do RoleService kiểm.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_ROLE", ("PARENT_ROLE_ID", "int NULL"));
        // Ten tai khoan dung de dang nhap thay cho email - xem User.UserName. Null voi moi tai khoan cu, nen
        // chi muc duy nhat phai loc bo NULL: SqlServer coi nhieu dong NULL la trung nhau.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_USER", ("USER_NAME", "nvarchar(50) NULL"));
        await ExecuteAsync(connection,
            "IF OBJECT_ID('HBE_USER') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes " +
            "WHERE name = 'IX_HBE_USER_USER_NAME' AND object_id = OBJECT_ID('HBE_USER')) " +
            "EXEC('CREATE UNIQUE INDEX IX_HBE_USER_USER_NAME ON HBE_USER (USER_NAME) WHERE USER_NAME IS NOT NULL')");
        // Luong cap duyet do Admin dinh nghia ma nguoi soan chon cho khoa hoc, kem so thu tu buoc dang cho duyet.
        // Co y KHONG kem khoa ngoai, cung ly do voi PARENT_ROLE_ID o tren: cot duoc them vao bang da co du lieu.
        // Tinh dung dan do CourseService / ApprovalWorkflowService kiem.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_COURSE",
            ("APPROVAL_WORKFLOW_ID", "int NULL"), ("CURRENT_STEP_ORDER", "int NOT NULL DEFAULT 0"));
        // Ten buoc cua luong cap duyet trong lich su duyet - null voi khoa hoc chay chuoi duyet co dinh cu.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_COURSE_APPROVAL_HISTORY", ("STEP_NAME", "nvarchar(100) NULL"));
        // Cach chon nguoi duyet cua tung buoc + nguoi duyet Admin ghim san (xem StepApproverMode). Mac dinh 0 =
        // AnyOfRole nen moi luong da cau hinh tu truoc giu nguyen hanh vi cu: gui cho ca nhom giu vai tro.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_APPROVAL_WORKFLOW_STEP",
            ("APPROVER_MODE", "int NOT NULL DEFAULT 0"), ("FIXED_USER_ID", "int NULL"));
        // Luong duyet duoc phan loai theo phong ban; NULL = dung chung, dung lam mac dinh cho luong da co san.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_APPROVAL_WORKFLOW",
            ("DEPARTMENT_ID", "int NULL"));
        // Diem thuong cua nhiem vu tuan, do quan tri vien dat - xem WeeklyMission.RewardPoint. Chi can thiet
        // cho db lo tao bang nhiem vu o ban chua co cot nay; db moi da co san tu EnsureNewTableAsync.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_WEEKLY_MISSION", ("REWARD_POINT", "int NOT NULL DEFAULT 0"));
        // Khuyen mai cua hang: gia niem yet truoc khi giam + dot khuyen mai da ap cho luot doi do.
        // Bang luot doi ra doi TRUOC tinh nang khuyen mai nen da ton tai ma thieu ba cot nay.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_SHOP_REDEMPTION",
            ("ORIGINAL_POINT_COST", "int NOT NULL DEFAULT 0"),
            ("SHOP_PROMOTION_ID", "int NULL"),
            ("PROMOTION_NAME", "nvarchar(150) NULL"));
        // Luot doi ghi truoc khi co khuyen mai deu la doi gia niem yet, nen gia goc bang dung gia da tra.
        // De nguyen 0 thi man hinh lich su bao ho "tiet kiem -X diem". Gia niem yet luon >= 1 (xem
        // ShopProductRequest.PointCost) nen dieu kien = 0 chi bat dung nhung dong chua duoc va.
        await ExecuteAsync(connection,
            "UPDATE HBE_SHOP_REDEMPTION SET ORIGINAL_POINT_COST = UNIT_POINT_COST WHERE ORIGINAL_POINT_COST = 0");
        // Nguon cua mot doan trong kho tri thuc AI + van tay noi dung goc — xem ban Sqlite ben tren. Bang kho
        // tri thuc ra doi TRUOC tinh nang "AI hoc noi dung khoa hoc" nen da ton tai ma thieu hai cot nay.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_DOCUMENT_CHUNK",
            ("SOURCE_KIND", "int NOT NULL DEFAULT 0"), ("SOURCE_HASH", "nvarchar(64) NULL"));

        // Hop chat noi bo gui duoc anh / tep / thu muc / nhan dan / ghi am. Bang tin nhan ra doi TRUOC ba tinh
        // nang do nen chi co cot ATTACHMENT_URL tran. MESSAGE_TYPE mac dinh 0 = Text, dung voi moi tin da luu.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_CHAT_MESSAGE",
            ("MESSAGE_TYPE", "int NOT NULL DEFAULT 0"),
            ("ATTACHMENT_NAME", "nvarchar(300) NULL"),
            ("ATTACHMENT_SIZE", "bigint NOT NULL DEFAULT 0"));
        // Anh dai dien cua NHOM + nen khung chat rieng cua tung thanh vien. Deu la NULL voi moi hoi thoai da co,
        // dung nghia "nhom chua dat anh" va "dung nen mac dinh".
        await AddMissingSqlServerColumnListAsync(connection, "HBE_CONVERSATION",
            ("AVATAR_URL", "nvarchar(500) NULL"));
        await AddMissingSqlServerColumnListAsync(connection, "HBE_CONVERSATION_MEMBER",
            ("BACKGROUND_KEY", "nvarchar(40) NULL"), ("BACKGROUND_URL", "nvarchar(500) NULL"));
        // Khoa tai khoan sau 3 lan go sai mat khau lien tiep - xem User.FailedLoginCount / User.LockedOutAt.
        // Moi tai khoan da co deu bat dau tu 0 lan sai va chua bi khoa, dung voi trang thai that cua chung.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_USER",
            ("FAILED_LOGIN_COUNT", "int NOT NULL DEFAULT 0"), ("LOCKED_OUT_AT", "datetime2 NULL"));
        // Anh bia trang tai khoan - xem User.CoverUrl. Null = anh bia mac dinh.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_USER", ("COVER_URL", "nvarchar(500) NULL"));
        // Ngay bat dau / ket thuc khoa hoc - bat buoc, xem Course.StartDate. Kem DEFAULT de ALTER chay duoc tren
        // bang da co du lieu, roi va ngay moi khoa hoc cu: bat dau tu ngay tao, ket thuc sau do mot nam. Moc
        // 0001-01-01 khong the do nguoi dung nhap ra nen dung lam dau hieu "dong chua duoc va".
        await AddMissingSqlServerColumnListAsync(connection, "HBE_COURSE",
            ("START_DATE", "datetime2 NOT NULL DEFAULT '0001-01-01'"),
            ("END_DATE", "datetime2 NOT NULL DEFAULT '0001-01-01'"));
        await ExecuteAsync(connection,
            "UPDATE HBE_COURSE SET START_DATE = CAST(CAST(CREATED_AT AS date) AS datetime2), " +
            "END_DATE = DATEADD(year, 1, CAST(CAST(CREATED_AT AS date) AS datetime2)) WHERE START_DATE = '0001-01-01'");
        // Buoc hoc goi SCORM trong lo trinh - null = buoc nay khong phai SCORM. Khong kem khoa ngoai, cung ly do
        // voi PARENT_ROLE_ID: cot them vao bang da co du lieu; ScormService tu kiem tinh dung dan.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_PATH_ITEM", ("SCORM_PACKAGE_ID", "int NULL"));
        // Lo trinh nang thanh SCORM Organization: chuong (muc cha), ten chuong, an khoi muc luc, gioi han luot,
        // nguong hoan thanh. Mac dinh deu giu nguyen hanh vi cu: nam o goc, hien, khong gioi han.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_PATH_ITEM",
            ("PARENT_ITEM_ID", "int NULL"), ("TITLE", "nvarchar(250) NULL"), ("IS_VISIBLE", "bit NOT NULL DEFAULT 1"),
            ("ATTEMPT_LIMIT", "int NULL"), ("COMPLETION_THRESHOLD", "decimal(5,4) NULL"));
        // Goi SCORM nhap tu ngoai (0) hay goi cua chinh khoa hoc (1) - moi goi da nhap truoc day deu la goi ngoai.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_SCORM_PACKAGE",
            ("KIND", "int NOT NULL DEFAULT 0"), ("UPDATED_AT", "datetime2 NULL"));
        // Chung nhan ben ngoai do AI doi chieu: quyet dinh cua AI hay Admin, AI doc duoc gi, so lan doc hong.
        // Giay da duyet tay truoc day giu IS_AI_REVIEWED = 0 - dung la Admin chot.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_EXTERNAL_CERTIFICATE",
            ("IS_AI_REVIEWED", "bit NOT NULL DEFAULT 0"), ("AI_RESULT_JSON", "nvarchar(max) NULL"),
            ("AI_ATTEMPT_COUNT", "int NOT NULL DEFAULT 0"));
        // Ghi hình tạm dừng được: thời lượng thật (không tính đoạn tạm dừng) + lúc chốt Bản ghi; và Diễn biến gắn
        // với Bản ghi nào. Cả ba cho phép rỗng nên mọi dòng cũ giữ nguyên. Cố ý KHÔNG kèm khóa ngoại cho RECORDING_ID.
        await AddMissingSqlServerColumnListAsync(connection, "HBE_ONLINE_CLASS_ROOM_RECORDING",
            ("DURATION_MS", "bigint NULL"), ("STOPPED_AT", "datetime2 NULL"));
        await AddMissingSqlServerColumnListAsync(connection, "HBE_ONLINE_CLASS_ROOM_EVENT", ("RECORDING_ID", "int NULL"));
    }

    /// <summary>Chạy một câu lệnh vá dữ liệu, bỏ qua lỗi "bảng/cột chưa tồn tại".
    ///
    /// Bảng chưa có (db mới toanh, vừa được EnsureCreated dựng đủ và đang rỗng) thì không có gì để vá — nhưng
    /// một câu UPDATE ném lỗi ở đây sẽ làm ứng dụng không khởi động nổi, chỉ vì một việc dọn dẹp không bắt buộc.</summary>
    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        catch (System.Data.Common.DbException) { /* bảng/cột chưa tồn tại — không có gì để vá */ }
    }

    /// <summary>Thêm các cột chỉ định (tên, kiểu SqlServer) còn thiếu trên một bảng — bỏ qua cột đã có sẵn.</summary>
    private static async Task AddMissingSqlServerColumnListAsync(System.Data.Common.DbConnection connection, string table,
        params (string Name, string Type)[] columnList)
    {
        using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText =
            "SELECT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = @table";
        var tableParameter = infoCommand.CreateParameter();
        tableParameter.ParameterName = "@table";
        tableParameter.Value = table;
        infoCommand.Parameters.Add(tableParameter);

        var existingColumnList = new List<string>();
        using (var reader = await infoCommand.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                existingColumnList.Add(reader.GetString(0));

        // Bảng chưa tồn tại (db mới toanh, EnsureCreated vừa dựng đủ) thì không có gì để vá.
        if (existingColumnList.Count == 0) return;

        foreach (var (name, type) in columnList)
        {
            if (existingColumnList.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE [{table}] ADD [{name}] {type}";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Thêm các cột còn thiếu trên những bảng đã tồn tại từ trước của một db Sqlite (không đụng tới cột/dữ liệu đã có).</summary>
    private static async Task EnsureNewColumnAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // Ngưỡng của hoạt động cần đo lường (số phút học tối thiểu trong ngày) — xem PointRule.Threshold.
        await AddMissingColumnListAsync(connection, "HBE_POINT_RULE", ("THRESHOLD", "INTEGER NULL"));
        // Khuyến mãi cửa hàng — xem chú thích ở bản SqlServer bên trên.
        await AddMissingColumnListAsync(connection, "HBE_SHOP_REDEMPTION",
            ("ORIGINAL_POINT_COST", "INTEGER NOT NULL DEFAULT 0"),
            ("SHOP_PROMOTION_ID", "INTEGER NULL"),
            ("PROMOTION_NAME", "TEXT NULL"));
        await ExecuteAsync(connection,
            "UPDATE HBE_SHOP_REDEMPTION SET ORIGINAL_POINT_COST = UNIT_POINT_COST WHERE ORIGINAL_POINT_COST = 0");
        await AddMissingColumnListAsync(connection, "HBE_PATH_ITEM", ("START_DATE", "TEXT NULL"), ("END_DATE", "TEXT NULL"));
        // Lý do từ chối bắt buộc + vai trò đã từ chối (chuỗi duyệt nội dung khóa học).
        await AddMissingColumnListAsync(connection, "HBE_COURSE", ("REJECTION_REASON", "TEXT NULL"), ("REJECTED_BY_ROLE_NAME", "TEXT NULL"));
        // Liên kết thông báo tới khóa học trực tiếp (không qua Enrollment) — dùng cho thông báo duyệt/từ chối nội dung.
        await AddMissingColumnListAsync(connection, "HBE_NOTIFICATION", ("COURSE_ID", "INTEGER NULL"));
        // Bài kiểm tra gắn theo một bài học ("con" của bài học) — null = bài kiểm tra độc lập.
        await AddMissingColumnListAsync(connection, "HBE_QUIZ", ("LESSON_ID", "INTEGER NULL"));
        // Đánh dấu phòng Nhân sự (duyệt xuất bản cuối cùng) bằng cờ riêng thay vì suy đoán qua mã phòng ban —
        // mã phòng ban do hệ thống tự sinh từ tên tiếng Việt nên gần như không bao giờ trùng đúng chữ "HR".
        await AddMissingColumnListAsync(connection, "HBE_DEPARTMENT", ("IS_HR_DEPARTMENT", "INTEGER NOT NULL DEFAULT 0"));
        // Bước mô phỏng 3D trong lộ trình — null = bước này không phải mô phỏng.
        await AddMissingColumnListAsync(connection, "HBE_PATH_ITEM", ("SCENARIO_SCENE_ID", "INTEGER NULL"));
        // Bối cảnh 3D của mô phỏng (công trường / văn phòng nhân sự...) + cờ bắt buộc làm theo đúng thứ tự bước.
        // Mặc định 'construction' để các kịch bản đã tạo trước đó giữ nguyên cảnh công trường như cũ.
        await AddMissingColumnListAsync(connection, "HBE_SCENARIO_SCENE",
            ("SCENE_KEY", "TEXT NOT NULL DEFAULT 'construction'"), ("IS_SEQUENTIAL", "INTEGER NOT NULL DEFAULT 0"));
        // Bối cảnh do người soạn bài tự tải lên (SceneKey = 'custom'): đường dẫn tệp + loại tệp (glb / ảnh 360°
        // / video 360° / ảnh thường). Null với mọi kịch bản cũ vì chúng dùng bối cảnh dựng sẵn.
        await AddMissingColumnListAsync(connection, "HBE_SCENARIO_SCENE",
            ("ASSET_URL", "TEXT NULL"), ("ASSET_TYPE", "TEXT NULL"));
        // Ảnh/video minh họa cho từng tình huống của mô phỏng.
        await AddMissingColumnListAsync(connection, "HBE_SCENARIO_NODE", ("MEDIA_URL", "TEXT NULL"));
        // Người thực sự xử lý bước duyệt cuối (Giám Đốc Nhân sự hoặc người được ủy quyền) — tên chỉ lộ cho
        // Giám Đốc Nhân sự xem, vai trò khác luôn thấy "Giám Đốc Nhân sự" (CourseService tự tính khi trả dữ liệu).
        await AddMissingColumnListAsync(connection, "HBE_COURSE",
            ("HR_APPROVER_USER_ID", "INTEGER NULL"), ("HR_APPROVER_IS_DELEGATE", "INTEGER NOT NULL DEFAULT 0"));
        // Mã tham gia phòng học online — chỉ người có mã/liên kết mới vào được buổi học thực sự (ClassRoomHub.JoinRoom).
        await AddMissingColumnListAsync(connection, "HBE_ONLINE_CLASS_ROOM", ("JOIN_CODE", "TEXT NULL"));
        // Ghi hình tạm dừng được — xem bản SqlServer bên trên.
        await AddMissingColumnListAsync(connection, "HBE_ONLINE_CLASS_ROOM_RECORDING",
            ("DURATION_MS", "INTEGER NULL"), ("STOPPED_AT", "TEXT NULL"));
        await AddMissingColumnListAsync(connection, "HBE_ONLINE_CLASS_ROOM_EVENT", ("RECORDING_ID", "INTEGER NULL"));
        // Câu hỏi chèn trong video: giới hạn số lần trả lời + điểm của câu hỏi. Hết lượt mà vẫn sai thì video
        // chạy tiếp nhưng câu đó bị 0 điểm (xem VideoLearningService.Answer).
        // Lưu ý kiểu cột: EF Core ánh xạ decimal sang TEXT trên Sqlite, không phải REAL/NUMERIC.
        await AddMissingColumnListAsync(connection, "HBE_VIDEO_QUESTION",
            ("MAX_ATTEMPT", "INTEGER NOT NULL DEFAULT 0"), ("SCORE", "TEXT NOT NULL DEFAULT '1'"));
        await AddMissingColumnListAsync(connection, "HBE_VIDEO_QUESTION_ANSWER",
            ("EARNED_SCORE", "TEXT NOT NULL DEFAULT '0'"), ("IS_FORCED_PASS", "INTEGER NOT NULL DEFAULT 0"));

        await AddMissingColumnListAsync(connection, "HBE_USER", ("FUNCTION_KEYS", "TEXT NULL"));
        // Ten tai khoan dung de dang nhap thay cho email - xem User.UserName. Null voi moi tai khoan cu.
        await AddMissingColumnListAsync(connection, "HBE_USER", ("USER_NAME", "TEXT NULL"));
        // EnsureCreated khong bo sung chi muc cho bang da ton tai, ma tinh duy nhat cua ten tai khoan la dieu
        // kien de tra cuu luc dang nhap ra dung MOT nguoi. Ten chi muc dat trung voi ten EF tu sinh de db cu
        // va db moi giong het nhau.
        await ExecuteAsync(connection,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_HBE_USER_USER_NAME ON HBE_USER (USER_NAME)");
        // Khoa tai khoan sau 3 lan go sai mat khau lien tiep - xem ban SqlServer ben tren.
        await AddMissingColumnListAsync(connection, "HBE_USER",
            ("FAILED_LOGIN_COUNT", "INTEGER NOT NULL DEFAULT 0"), ("LOCKED_OUT_AT", "TEXT NULL"));
        // Anh bia trang tai khoan - xem ban SqlServer ben tren.
        await AddMissingColumnListAsync(connection, "HBE_USER", ("COVER_URL", "TEXT NULL"));
        // Quyền mặc định của vai trò + cờ vai trò cài sẵn (không cho đổi tên/xóa) — xem màn hình "Vai trò".
        await AddMissingColumnListAsync(connection, "HBE_ROLE",
            ("FUNCTION_KEYS", "TEXT NULL"), ("IS_SYSTEM", "INTEGER NOT NULL DEFAULT 0"), ("IS_ACTIVE", "INTEGER NOT NULL DEFAULT 1"));
        // Cách tua video của bài học (0 = tua tự do, giữ nguyên hành vi của mọi bài học đã tạo từ trước)
        // + mốc xa nhất học viên đã xem, phục vụ chế độ "bắt buộc xem hết" (xem VideoSeekPolicy).
        await AddMissingColumnListAsync(connection, "HBE_LESSON", ("VIDEO_SEEK_POLICY", "INTEGER NOT NULL DEFAULT 0"));
        await AddMissingColumnListAsync(connection, "HBE_LESSON_PROGRESS", ("MAX_WATCHED_SECOND", "INTEGER NOT NULL DEFAULT 0"));
        // Luong cap duyet do Admin dinh nghia ma nguoi soan chon cho khoa hoc, kem so thu tu buoc dang cho duyet.
        // NULL / 0 = khoa hoc khong chon luong nao, van chay chuoi duyet co dinh cu.
        await AddMissingColumnListAsync(connection, "HBE_COURSE",
            ("APPROVAL_WORKFLOW_ID", "INTEGER NULL"), ("CURRENT_STEP_ORDER", "INTEGER NOT NULL DEFAULT 0"));
        // Ten buoc cua luong cap duyet trong lich su duyet - null voi khoa hoc chay chuoi duyet co dinh cu.
        await AddMissingColumnListAsync(connection, "HBE_COURSE_APPROVAL_HISTORY", ("STEP_NAME", "TEXT NULL"));
        // Cach chon nguoi duyet cua tung buoc + nguoi duyet Admin ghim san (xem StepApproverMode). Mac dinh 0 =
        // AnyOfRole nen moi luong da cau hinh tu truoc giu nguyen hanh vi cu: gui cho ca nhom giu vai tro.
        await AddMissingColumnListAsync(connection, "HBE_APPROVAL_WORKFLOW_STEP",
            ("APPROVER_MODE", "INTEGER NOT NULL DEFAULT 0"), ("FIXED_USER_ID", "INTEGER NULL"));
        await AddMissingColumnListAsync(connection, "HBE_APPROVAL_WORKFLOW",
            ("DEPARTMENT_ID", "INTEGER NULL"));
        // Diem thuong cua nhiem vu tuan, do quan tri vien dat - xem WeeklyMission.RewardPoint.
        await AddMissingColumnListAsync(connection, "HBE_WEEKLY_MISSION", ("REWARD_POINT", "INTEGER NOT NULL DEFAULT 0"));
        // Nguon cua mot doan trong kho tri thuc AI + van tay noi dung goc. Mac dinh 0 = tai lieu tai len, dung
        // voi moi doan da nap tu truoc; noi dung khoa hoc do may dong bo se tu ghi 1/2 vao day (xem
        // KnowledgeSourceKind va CourseKnowledgeService).
        await AddMissingColumnListAsync(connection, "HBE_DOCUMENT_CHUNK",
            ("SOURCE_KIND", "INTEGER NOT NULL DEFAULT 0"), ("SOURCE_HASH", "TEXT NULL"));
        // Dinh kem cua hop chat noi bo — xem ban SqlServer ben tren.
        await AddMissingColumnListAsync(connection, "HBE_CHAT_MESSAGE",
            ("MESSAGE_TYPE", "INTEGER NOT NULL DEFAULT 0"),
            ("ATTACHMENT_NAME", "TEXT NULL"),
            ("ATTACHMENT_SIZE", "INTEGER NOT NULL DEFAULT 0"));
        // Anh nhom + nen khung chat — xem ban SqlServer ben tren.
        await AddMissingColumnListAsync(connection, "HBE_CONVERSATION", ("AVATAR_URL", "TEXT NULL"));
        await AddMissingColumnListAsync(connection, "HBE_CONVERSATION_MEMBER",
            ("BACKGROUND_KEY", "TEXT NULL"), ("BACKGROUND_URL", "TEXT NULL"));
        // Ngày bắt đầu / kết thúc khóa học — xem bản SqlServer bên trên. date() của Sqlite đọc được cả chuỗi
        // ngày giờ có 7 chữ số lẻ mà EF ghi vào CREATED_AT.
        await AddMissingColumnListAsync(connection, "HBE_COURSE",
            ("START_DATE", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
            ("END_DATE", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"));
        await ExecuteAsync(connection,
            "UPDATE HBE_COURSE SET START_DATE = date(CREATED_AT) || ' 00:00:00', " +
            "END_DATE = date(CREATED_AT, '+1 year') || ' 00:00:00' WHERE START_DATE LIKE '0001-01-01%'");
        // Bước học gói SCORM trong lộ trình — xem bản SqlServer bên trên.
        await AddMissingColumnListAsync(connection, "HBE_PATH_ITEM", ("SCORM_PACKAGE_ID", "INTEGER NULL"));
        // Lộ trình nâng thành SCORM Organization — xem bản SqlServer bên trên.
        await AddMissingColumnListAsync(connection, "HBE_PATH_ITEM",
            ("PARENT_ITEM_ID", "INTEGER NULL"), ("TITLE", "TEXT NULL"), ("IS_VISIBLE", "INTEGER NOT NULL DEFAULT 1"),
            ("ATTEMPT_LIMIT", "INTEGER NULL"), ("COMPLETION_THRESHOLD", "TEXT NULL"));
        await AddMissingColumnListAsync(connection, "HBE_SCORM_PACKAGE",
            ("KIND", "INTEGER NOT NULL DEFAULT 0"), ("UPDATED_AT", "TEXT NULL"));
        // Chứng nhận bên ngoài do AI đối chiếu — xem bản SqlServer bên trên.
        await AddMissingColumnListAsync(connection, "HBE_EXTERNAL_CERTIFICATE",
            ("IS_AI_REVIEWED", "INTEGER NOT NULL DEFAULT 0"), ("AI_RESULT_JSON", "TEXT NULL"),
            ("AI_ATTEMPT_COUNT", "INTEGER NOT NULL DEFAULT 0"));
    }

    /// <summary>Thêm các cột chỉ định (tên, kiểu Sqlite) còn thiếu trên một bảng đã tồn tại — bỏ qua cột đã có sẵn.</summary>
    private static async Task AddMissingColumnListAsync(System.Data.Common.DbConnection connection, string table, params (string Name, string Type)[] columnList)
    {
        using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = $"PRAGMA table_info('{table}')";
        using var reader = await infoCommand.ExecuteReaderAsync();
        var existingColumnList = new List<string>();
        while (await reader.ReadAsync())
            existingColumnList.Add(reader.GetString(reader.GetOrdinal("name")));
        await reader.DisposeAsync();

        foreach (var (name, type) in columnList)
        {
            if (existingColumnList.Contains(name)) continue;

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {type}";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }
}
