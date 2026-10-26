using HbELearning.Application.Common.Results;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.OnlineClassRooms;

/// <summary>Ai được bắt đầu một Bản ghi của Buổi học: chỉ Chủ trì (xem CONTEXT.md).</summary>
public class RecordingPermissionTests
{
    [Fact]
    public async Task Chu_tri_bat_dau_duoc_ban_ghi_trong_buoi_hoc_dang_dien_ra()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        world.ActAs(world.Host);

        var result = await world.Rooms.StartRecording(roomId);

        Assert.True(result.IsSuccess);
        Assert.Single(world.RecordingsOf(roomId));
    }

    [Fact]
    public async Task Hoc_vien_khong_bat_dau_duoc_ban_ghi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        world.ActAs(world.Student);

        var result = await world.Rooms.StartRecording(roomId);

        Assert.Equal(ResultKind.Forbidden, result.Kind);
        Assert.Empty(world.RecordingsOf(roomId));
    }

    [Fact]
    public async Task Admin_khong_phai_chu_tri_cung_khong_bat_dau_duoc_ban_ghi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        world.ActAs(world.Admin);

        var result = await world.Rooms.StartRecording(roomId);

        Assert.Equal(ResultKind.Forbidden, result.Kind);
        Assert.Empty(world.RecordingsOf(roomId));
    }

    [Fact]
    public async Task Khong_ghi_hinh_duoc_khi_buoi_hoc_chua_dien_ra()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        using (var db = world.NewDb())
        {
            var room = db.OnlineClassRoom.Single(r => r.Id == roomId);
            room.Status = HbELearning.Domain.Enums.OnlineClassRoomStatus.Ended;
            db.SaveChanges();
        }
        world.ActAs(world.Host);

        var result = await world.Rooms.StartRecording(roomId);

        Assert.Equal(ResultKind.Invalid, result.Kind);
        Assert.Empty(world.RecordingsOf(roomId));
    }
}
