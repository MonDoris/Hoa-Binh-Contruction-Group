using HbELearning.Application.DTOs;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.OnlineClassRooms;

/// <summary>Danh sách Lịch sử: các Buổi học đã kết thúc mà người xem được phép xem, kèm số liệu và bộ lọc.</summary>
public class HistoryListTests
{
    private static async Task<List<OnlineClassRoomHistoryDto>> History(ClassRoomWorld world, OnlineClassRoomHistoryQuery? query = null)
        => ((IEnumerable<OnlineClassRoomHistoryDto>)(await world.Rooms.GetHistory(query ?? new())).Value!).ToList();

    [Fact]
    public async Task Lich_su_chi_gom_buoi_da_ket_thuc_va_duoc_phep_xem_moi_nhat_truoc()
    {
        using var world = ClassRoomWorld.Create();
        var older = world.AddEndedRoom("Cũ", joined: new[] { world.Student }, startedAt: new DateTime(2026, 9, 1, 9, 0, 0));
        var newer = world.AddEndedRoom("Mới", joined: new[] { world.Student }, startedAt: new DateTime(2026, 10, 1, 9, 0, 0));
        world.AddEndedRoom("Không có tôi", startedAt: new DateTime(2026, 10, 5, 9, 0, 0));
        world.AddLiveRoom("Đang diễn ra"); // chưa kết thúc: không phải Lịch sử
        world.ActAs(world.Student);

        var list = await History(world);

        Assert.Equal(new[] { newer, older }, list.Select(r => r.Id));
    }

    [Fact]
    public async Task Lich_su_tinh_dung_thoi_luong_so_nguoi_so_ban_ghi_va_dung_luong()
    {
        using var world = ClassRoomWorld.Create();
        var other = world.AddPerson("P2", "Người Hai");
        var roomId = world.AddEndedRoom("Buổi số liệu", joined: new[] { world.Student, other },
            duration: TimeSpan.FromMinutes(72), recordingSizes: new long[] { 1_000, 2_500 });
        // Học viên vào lại lần hai: vẫn tính là MỘT người.
        using (var db = world.NewDb())
        {
            db.OnlineClassRoomEvent.Add(new HbELearning.Domain.Entities.OnlineClassRoomEvent
            {
                OnlineClassRoomId = roomId, EventType = HbELearning.Domain.Enums.OnlineClassRoomEventType.Joined,
                UserId = world.Student.Id, UserName = world.Student.FullName,
            });
            db.SaveChanges();
        }
        world.ActAs(world.Host);

        var item = Assert.Single(await History(world));

        Assert.Equal(TimeSpan.FromMinutes(72).TotalMilliseconds, item.DurationMs);
        Assert.Equal(3, item.ParticipantCount); // chủ trì + 2 người, không đếm lượt vào lại
        Assert.Equal(2, item.RecordingCount);
        Assert.Equal(3_500, item.RecordingSizeBytes);
        Assert.Equal(world.Host.FullName, item.HostUserName);
        Assert.True(item.IsHost);
    }

    [Fact]
    public async Task Loc_theo_tu_khoa_trong_ten_buoi_hoc_va_ten_chu_tri()
    {
        using var world = ClassRoomWorld.Create();
        var byTitle = world.AddEndedRoom("An toàn PCCC", joined: new[] { world.Student });
        var byHost = world.AddEndedRoom("Họp nhóm", host: world.AddPerson("GV1", "Nguyễn Giảng Viên"), joined: new[] { world.Student });
        world.AddEndedRoom("Chuyện khác", joined: new[] { world.Student });
        world.ActAs(world.Student);

        Assert.Equal(new[] { byTitle }, (await History(world, new() { Keyword = "pccc" })).Select(r => r.Id));
        Assert.Equal(new[] { byHost }, (await History(world, new() { Keyword = "giảng viên" })).Select(r => r.Id));
    }

    [Fact]
    public async Task Loc_theo_khoang_ngay_bao_gom_ca_ngay_cuoi()
    {
        using var world = ClassRoomWorld.Create();
        world.AddEndedRoom("Trước", joined: new[] { world.Student }, startedAt: new DateTime(2026, 9, 30, 23, 0, 0));
        var inFirstDay = world.AddEndedRoom("Đầu", joined: new[] { world.Student }, startedAt: new DateTime(2026, 10, 1, 8, 0, 0));
        var inLastDay = world.AddEndedRoom("Cuối", joined: new[] { world.Student }, startedAt: new DateTime(2026, 10, 7, 22, 30, 0));
        world.AddEndedRoom("Sau", joined: new[] { world.Student }, startedAt: new DateTime(2026, 10, 8, 0, 30, 0));
        world.ActAs(world.Student);

        var list = await History(world, new() { From = new DateTime(2026, 10, 1), To = new DateTime(2026, 10, 7) });

        Assert.Equal(new[] { inLastDay, inFirstDay }, list.Select(r => r.Id));
    }

    [Fact]
    public async Task Loc_chi_buoi_cua_toi_thu_hep_danh_sach_cua_admin()
    {
        using var world = ClassRoomWorld.Create();
        var asHost = world.AddEndedRoom("Tôi chủ trì", host: world.Admin);
        var asGuest = world.AddEndedRoom("Tôi có vào", joined: new[] { world.Admin });
        world.AddEndedRoom("Của người khác", joined: new[] { world.Student });
        world.ActAs(world.Admin);

        Assert.Equal(3, (await History(world)).Count); // Admin thấy tất cả
        var mine = await History(world, new() { OnlyMine = true });

        Assert.Equal(new[] { asHost, asGuest }.OrderBy(x => x), mine.Select(r => r.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Cac_bo_loc_ket_hop_duoc_voi_nhau()
    {
        using var world = ClassRoomWorld.Create();
        var match = world.AddEndedRoom("PCCC tháng 10", joined: new[] { world.Student }, startedAt: new DateTime(2026, 10, 3, 9, 0, 0));
        world.AddEndedRoom("PCCC tháng 9", joined: new[] { world.Student }, startedAt: new DateTime(2026, 9, 3, 9, 0, 0));
        world.AddEndedRoom("Họp tháng 10", joined: new[] { world.Student }, startedAt: new DateTime(2026, 10, 4, 9, 0, 0));
        world.ActAs(world.Student);

        var list = await History(world, new()
        { Keyword = "pccc", From = new DateTime(2026, 10, 1), To = new DateTime(2026, 10, 31), OnlyMine = true });

        Assert.Equal(new[] { match }, list.Select(r => r.Id));
    }

    [Fact]
    public async Task Lich_su_trong_tra_ve_danh_sach_rong_chu_khong_loi()
    {
        using var world = ClassRoomWorld.Create();
        world.ActAs(world.Student);

        Assert.Empty(await History(world));
    }
}
