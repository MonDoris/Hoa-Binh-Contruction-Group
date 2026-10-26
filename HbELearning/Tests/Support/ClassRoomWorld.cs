using HbELearning.Application;
using HbELearning.Application.Common.Interfaces;
using HbELearning.Application.DTOs;
using HbELearning.Application.Features.OnlineClassRooms;
using HbELearning.Domain.Constants;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HbELearning.Tests.Support;

/// <summary>
/// Một "thế giới" nhỏ để kiểm thử phòng học online: DI thật của lớp Application, chạy trên SQLite trong bộ nhớ.
/// Chỉ những cổng nhìn ra ngoài (người dùng hiện tại, thư mục, xử lý media, tệp, thời gian) mới là bản giả.
///
/// Mỗi lần gọi dịch vụ mở một scope mới — giống một yêu cầu HTTP hay một lệnh của Hub — nên không có trạng thái
/// EF Core nào "rò" từ lệnh này sang lệnh kia và làm kết quả đẹp hơn thực tế.
/// </summary>
public sealed class ClassRoomWorld : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ServiceProvider _provider;
    private readonly string _webRoot;

    public ManualTimeProvider Clock { get; } = new();
    public TestCurrentUser Me { get; } = new();
    public FakeMediaFileProcessor Media { get; } = new();

    public User Host { get; private set; } = null!;
    public User Student { get; private set; } = null!;
    public User Outsider { get; private set; } = null!;
    public User Admin { get; private set; } = null!;

    private ClassRoomWorld(SqliteConnection connection, DbContextOptions<AppDbContext> options, string webRoot)
    {
        _connection = connection;
        _options = options;
        _webRoot = webRoot;

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(Me);
        services.AddSingleton<IMediaFileProcessor>(Media);
        services.AddSingleton<IAppPaths>(new TestAppPaths(webRoot));
        services.AddSingleton<IFileService, NullFileService>();
        services.AddSingleton<ICertificateQrCodeGenerator, FakeQrCodeGenerator>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IAppDbContext>(_ => new AppDbContext(options));
        services.AddApplication();
        _provider = services.BuildServiceProvider();
    }

    public static ClassRoomWorld Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        var webRoot = Path.Combine(Path.GetTempPath(), "hbe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);

        using (var db = new AppDbContext(options)) db.Database.EnsureCreated();

        var world = new ClassRoomWorld(connection, options, webRoot);
        world.SeedPeople();
        return world;
    }

    private void SeedPeople()
    {
        using var db = NewDb();
        var learnerRoleId = db.Role.Single(r => r.Name == RoleName.Learner).Id;
        var adminRoleId = db.Role.Single(r => r.Name == RoleName.Admin).Id;

        User Person(string code, string name, int roleId) => new()
        {
            EmployeeCode = code, FullName = name, Email = $"{code.ToLowerInvariant()}@test.local",
            PasswordHash = "x", RoleId = roleId,
        };

        Host = Person("HOST", "Chủ Trì", learnerRoleId);
        Student = Person("STU", "Học Viên", learnerRoleId);
        Outsider = Person("OUT", "Người Ngoài", learnerRoleId);
        Admin = Person("ADM", "Quản Trị", adminRoleId);
        db.User.AddRange(Host, Student, Outsider, Admin);
        db.SaveChanges();
    }

    /// <summary>DbContext riêng cho phần kiểm tra/dựng dữ liệu của bài test (không dùng chung với dịch vụ).</summary>
    public AppDbContext NewDb() => new(_options);

    /// <summary>Từ lúc này, các lời gọi dịch vụ chạy với tư cách người này.</summary>
    public void ActAs(User user)
    {
        Me.UserId = user.Id;
        Me.FullName = user.FullName;
        Me.IsAdmin = user.Id == Admin?.Id;
    }

    public ClassRoomAttendee Attendee(User user) => new(user.Id, user.FullName, user.Id == Admin.Id);

    public IOnlineClassRoomService Rooms => _provider.CreateScope().ServiceProvider.GetRequiredService<IOnlineClassRoomService>();
    public IClassRoomSessionService Session => _provider.CreateScope().ServiceProvider.GetRequiredService<IClassRoomSessionService>();

    /// <summary>Bất kỳ dịch vụ nào của lớp Application (mỗi lần gọi một scope mới, như một yêu cầu HTTP) — cho các
    /// bài test ngoài phòng học dùng chung "thế giới" SQLite này, không phải dựng lại DI.</summary>
    public T Service<T>() where T : notnull => _provider.CreateScope().ServiceProvider.GetRequiredService<T>();

    /// <summary>Dựng sẵn một buổi học đang diễn ra do <see cref="Host"/> chủ trì.</summary>
    public int AddLiveRoom(string title = "Buổi học thử")
    {
        using var db = NewDb();
        var room = new OnlineClassRoom
        {
            Title = title, HostUserId = Host.Id, JoinCode = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            ScheduledStart = Clock.GetLocalNow().DateTime, Status = OnlineClassRoomStatus.Live,
            ActualStartAt = Clock.GetLocalNow().DateTime,
        };
        db.OnlineClassRoom.Add(room);
        db.SaveChanges();
        return room.Id;
    }

    /// <summary>
    /// Dựng sẵn một Buổi học ĐÃ KẾT THÚC: Chủ trì cộng những người đã vào (mỗi người một dòng "vào phòng", có thể
    /// kèm "rời phòng"), cùng các Bản ghi nếu muốn. Dữ liệu đúng như Hub đã ghi, nên quy tắc xem Lịch sử đọc được nó.
    /// </summary>
    public int AddEndedRoom(string title, User? host = null, IEnumerable<User>? joined = null, TimeSpan? duration = null,
        DateTime? startedAt = null, params long[] recordingSizes)
    {
        host ??= Host;
        var start = startedAt ?? Clock.GetLocalNow().DateTime;
        using var db = NewDb();
        var room = new OnlineClassRoom
        {
            Title = title, HostUserId = host.Id, JoinCode = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            ScheduledStart = start, Status = OnlineClassRoomStatus.Ended,
            ActualStartAt = start, ActualEndAt = start + (duration ?? TimeSpan.FromMinutes(45)),
        };
        db.OnlineClassRoom.Add(room);
        db.SaveChanges();

        void Event(User who, OnlineClassRoomEventType type) => db.OnlineClassRoomEvent.Add(new OnlineClassRoomEvent
        {
            OnlineClassRoomId = room.Id, EventType = type, UserId = who.Id, UserName = who.FullName, CreatedAt = start,
        });

        Event(host, OnlineClassRoomEventType.Joined);
        foreach (var person in joined ?? Array.Empty<User>())
        {
            Event(person, OnlineClassRoomEventType.Joined);
            Event(person, OnlineClassRoomEventType.Left);
        }
        db.OnlineClassRoomEvent.Add(new OnlineClassRoomEvent
        {
            OnlineClassRoomId = room.Id, EventType = OnlineClassRoomEventType.ChatMessage, UserId = host.Id,
            UserName = host.FullName, Message = "Xin chào cả lớp", CreatedAt = start,
        });

        foreach (var size in recordingSizes)
            db.OnlineClassRoomRecording.Add(new OnlineClassRoomRecording
            {
                OnlineClassRoomId = room.Id, FileUrl = $"/uploads/classroom-recording/test/{Guid.NewGuid():N}.webm",
                FileSizeBytes = size, RecordedByUserId = host.Id, DurationMs = 60_000, StoppedAt = start,
            });
        db.SaveChanges();
        return room.Id;
    }

    public User AddPerson(string code, string name)
    {
        using var db = NewDb();
        var user = new User
        {
            EmployeeCode = code, FullName = name, Email = $"{code.ToLowerInvariant()}@test.local", PasswordHash = "x",
            RoleId = db.Role.Single(r => r.Name == RoleName.Learner).Id,
        };
        db.User.Add(user);
        db.SaveChanges();
        return user;
    }

    public string JoinCodeOf(int roomId)
    {
        using var db = NewDb();
        return db.OnlineClassRoom.Single(r => r.Id == roomId).JoinCode!;
    }

    public List<OnlineClassRoomEvent> EventsOf(int roomId)
    {
        using var db = NewDb();
        return db.OnlineClassRoomEvent.Where(e => e.OnlineClassRoomId == roomId).OrderBy(e => e.Id).ToList();
    }

    public List<OnlineClassRoomRecording> RecordingsOf(int roomId)
    {
        using var db = NewDb();
        return db.OnlineClassRoomRecording.Where(r => r.OnlineClassRoomId == roomId).OrderBy(r => r.Id).ToList();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_webRoot, recursive: true); } catch { /* thư mục tạm, bỏ qua */ }
    }
}
