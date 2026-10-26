using HbELearning.Application.Common.Results;
using HbELearning.Application.DTOs;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.OnlineClassRooms;

/// <summary>
/// Ai xem được Lịch sử của một Buổi học đã kết thúc: Chủ trì, Admin, và người từng vào buổi đó (có dòng "vào phòng"
/// trong Diễn biến). Người khác không thấy buổi đó trong danh sách và bị 403 khi mở Bản ghi hay Diễn biến của nó.
/// </summary>
public class HistoryAccessTests
{
    private static List<OnlineClassRoomListDto> Rooms(Result r) => ((IEnumerable<OnlineClassRoomListDto>)r.Value!).ToList();

    [Fact]
    public async Task Chu_tri_Admin_va_nguoi_tung_vao_xem_duoc_dien_bien_va_ban_ghi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi A", joined: new[] { world.Student }, recordingSizes: 1000);

        foreach (var viewer in new[] { world.Host, world.Admin, world.Student })
        {
            world.ActAs(viewer);
            Assert.True((await world.Rooms.GetEvents(roomId)).IsSuccess, $"{viewer.FullName} phải xem được Diễn biến");
            Assert.True((await world.Rooms.GetRecordings(roomId)).IsSuccess, $"{viewer.FullName} phải xem được Bản ghi");
            Assert.True((await world.Rooms.GetById(roomId)).IsSuccess, $"{viewer.FullName} phải mở được buổi học");
        }
    }

    [Fact]
    public async Task Nguoi_chua_tung_vao_bi_403_o_ca_ban_ghi_dien_bien_va_chi_tiet()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi họp nội bộ", joined: new[] { world.Student }, recordingSizes: 1000);
        world.ActAs(world.Outsider);

        Assert.Equal(ResultKind.Forbidden, (await world.Rooms.GetEvents(roomId)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await world.Rooms.GetRecordings(roomId)).Kind);
        Assert.Equal(ResultKind.Forbidden, (await world.Rooms.GetById(roomId)).Kind);
    }

    [Fact]
    public async Task Nguoi_vao_muon_roi_som_van_xem_toan_bo_ban_ghi_va_dien_bien()
    {
        using var world = ClassRoomWorld.Create();
        // Học viên chỉ có đúng hai dòng (vào, rời) nhưng buổi có thêm chat và hai Bản ghi diễn ra khi họ vắng mặt.
        var roomId = world.AddEndedRoom("Buổi dài", joined: new[] { world.Student }, recordingSizes: new long[] { 500, 700 });
        world.ActAs(world.Student);

        var events = (IEnumerable<OnlineClassRoomEventDto>)(await world.Rooms.GetEvents(roomId)).Value!;
        var recordings = (IEnumerable<OnlineClassRoomRecordingDto>)(await world.Rooms.GetRecordings(roomId)).Value!;

        Assert.Contains(events, e => e.EventType == "ChatMessage" && e.UserId == world.Host.Id);
        Assert.Equal(2, recordings.Count());
    }

    [Fact]
    public async Task Nguoi_chua_vao_phong_nhung_co_mat_trong_man_hinh_nhap_ma_van_bi_chan()
    {
        using var world = ClassRoomWorld.Create();
        // Chỉ có sự kiện "rời phòng" hoặc chat của người lạ không đủ: điều kiện là một dòng "vào phòng".
        var roomId = world.AddEndedRoom("Buổi B");
        using (var db = world.NewDb())
        {
            db.OnlineClassRoomEvent.Add(new HbELearning.Domain.Entities.OnlineClassRoomEvent
            {
                OnlineClassRoomId = roomId, EventType = OnlineClassRoomEventType.Left, UserId = world.Outsider.Id, UserName = "x",
            });
            db.SaveChanges();
        }
        world.ActAs(world.Outsider);

        Assert.Equal(ResultKind.Forbidden, (await world.Rooms.GetEvents(roomId)).Kind);
    }

    [Fact]
    public async Task Danh_sach_buoi_da_ket_thuc_chi_hien_buoi_nguoi_xem_duoc_phep()
    {
        using var world = ClassRoomWorld.Create();
        var joinedRoom = world.AddEndedRoom("Có tôi", joined: new[] { world.Student });
        var hostedRoom = world.AddEndedRoom("Tôi chủ trì", host: world.Student);
        var otherRoom = world.AddEndedRoom("Không có tôi");
        world.ActAs(world.Student);

        var ids = Rooms(await world.Rooms.GetList(null)).Select(r => r.Id).ToList();

        Assert.Contains(joinedRoom, ids);
        Assert.Contains(hostedRoom, ids);
        Assert.DoesNotContain(otherRoom, ids);

        world.ActAs(world.Admin);
        Assert.Equal(3, Rooms(await world.Rooms.GetList(OnlineClassRoomStatus.Ended)).Count);
    }

    [Fact]
    public async Task Buoi_chua_dien_ra_dang_dien_ra_va_da_huy_giu_nguyen_cho_moi_nguoi()
    {
        using var world = ClassRoomWorld.Create();
        var live = world.AddLiveRoom("Đang diễn ra");
        int scheduled, cancelled;
        using (var db = world.NewDb())
        {
            var a = new HbELearning.Domain.Entities.OnlineClassRoom
            { Title = "Sắp tới", HostUserId = world.Host.Id, JoinCode = "SCH001", ScheduledStart = DateTime.Now.AddDays(1), Status = OnlineClassRoomStatus.Scheduled };
            var b = new HbELearning.Domain.Entities.OnlineClassRoom
            { Title = "Đã hủy", HostUserId = world.Host.Id, JoinCode = "CAN001", ScheduledStart = DateTime.Now.AddDays(2), Status = OnlineClassRoomStatus.Cancelled };
            db.OnlineClassRoom.AddRange(a, b);
            db.SaveChanges();
            scheduled = a.Id; cancelled = b.Id;
        }
        world.ActAs(world.Outsider);

        var ids = Rooms(await world.Rooms.GetList(null)).Select(r => r.Id).ToList();

        Assert.Contains(live, ids);
        Assert.Contains(scheduled, ids);
        Assert.Contains(cancelled, ids);
        Assert.True((await world.Rooms.GetById(live)).IsSuccess);
    }
}
