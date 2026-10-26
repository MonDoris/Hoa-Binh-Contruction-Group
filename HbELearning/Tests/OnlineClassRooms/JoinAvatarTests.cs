using HbELearning.Tests.Support;

namespace HbELearning.Tests.OnlineClassRooms;

/// <summary>Vào phòng thì mang theo ảnh đại diện của người dùng, để mọi người trong phòng thấy ảnh thay vì chữ cái đầu.</summary>
public class JoinAvatarTests
{
    [Fact]
    public async Task Vao_phong_tra_ve_anh_dai_dien_cua_nguoi_vao()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        using (var db = world.NewDb())
        {
            db.User.Single(u => u.Id == world.Student.Id).AvatarUrl = "/uploads/avatar/hoc-vien.png";
            db.SaveChanges();
        }
        var code = world.JoinCodeOf(roomId);

        var decision = await world.Session.JoinAsync(roomId, code, world.Attendee(world.Student));

        Assert.Null(decision.Error);
        Assert.Equal("/uploads/avatar/hoc-vien.png", decision.AvatarUrl);
    }

    [Fact]
    public async Task Nguoi_chua_co_anh_dai_dien_thi_tra_ve_rong()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();

        var decision = await world.Session.JoinAsync(roomId, world.JoinCodeOf(roomId), world.Attendee(world.Student));

        Assert.Null(decision.Error);
        Assert.Null(decision.AvatarUrl);
    }
}
