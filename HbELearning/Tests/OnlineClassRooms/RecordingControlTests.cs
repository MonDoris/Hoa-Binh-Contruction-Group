using HbELearning.Application.DTOs;
using HbELearning.Application.Features.OnlineClassRooms;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.OnlineClassRooms;

/// <summary>Điều khiển ghi hình của Chủ trì trong một Buổi học đang diễn ra: Ghi, Tạm dừng, Tiếp tục, Dừng ghi.</summary>
public class RecordingControlTests
{
    /// <summary>Chủ trì bấm Ghi: tạo Bản ghi qua dịch vụ buổi học rồi báo cho phiên học bắt đầu chạy đồng hồ.</summary>
    private static async Task<int> HostStartsRecording(ClassRoomWorld world, int roomId)
    {
        world.ActAs(world.Host);
        var started = await world.Rooms.StartRecording(roomId);
        var recordingId = ((OnlineClassRoomRecordingDto)started.Value!).Id;
        var command = await world.Session.StartRecordingAsync(roomId, recordingId, world.Attendee(world.Host));
        Assert.True(command.Accepted);
        return recordingId;
    }

    [Fact]
    public async Task Dong_ho_ghi_hinh_khong_tinh_doan_tam_dung()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var host = world.Attendee(world.Host);
        await HostStartsRecording(world, roomId);

        world.Clock.AdvanceSeconds(10);
        var paused = await world.Session.PauseRecordingAsync(roomId, host);
        Assert.True(paused.Accepted);
        Assert.Equal(RecordingState.Paused, paused.Snapshot.State);
        Assert.Equal(10_000, paused.Snapshot.ElapsedMs);

        world.Clock.AdvanceSeconds(30); // đang tạm dừng: đồng hồ đứng yên
        Assert.Equal(10_000, world.Session.GetRecordingSnapshot(roomId).ElapsedMs);

        var resumed = await world.Session.ResumeRecordingAsync(roomId, host);
        Assert.True(resumed.Accepted);
        world.Clock.AdvanceSeconds(5);

        var now = world.Session.GetRecordingSnapshot(roomId);
        Assert.Equal(RecordingState.Recording, now.State);
        Assert.Equal(15_000, now.ElapsedMs);
    }

    [Fact]
    public async Task Dung_ghi_chot_ban_ghi_va_dua_trang_thai_ve_khong_ghi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        await HostStartsRecording(world, roomId);
        world.Clock.AdvanceSeconds(20);

        var stopped = await world.Session.StopRecordingAsync(roomId, world.Attendee(world.Host));

        Assert.True(stopped.Accepted);
        Assert.Equal(RecordingState.Idle, stopped.Snapshot.State);
        Assert.Equal(0, world.Session.GetRecordingSnapshot(roomId).ElapsedMs);
    }

    [Fact]
    public async Task Ghi_lai_sau_khi_dung_tao_ban_ghi_moi_va_dong_ho_ve_khong()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var first = await HostStartsRecording(world, roomId);
        world.Clock.AdvanceSeconds(20);
        await world.Session.StopRecordingAsync(roomId, world.Attendee(world.Host));

        var second = await HostStartsRecording(world, roomId);

        Assert.NotEqual(first, second);
        Assert.Equal(2, world.RecordingsOf(roomId).Count);
        Assert.Equal(0, world.Session.GetRecordingSnapshot(roomId).ElapsedMs);
        Assert.Equal(second, world.Session.GetRecordingSnapshot(roomId).RecordingId);
    }

    [Fact]
    public async Task Nguoi_khong_phai_chu_tri_khong_dieu_khien_duoc_ghi_hinh()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var recordingId = await HostStartsRecording(world, roomId);
        world.Clock.AdvanceSeconds(10);
        var student = world.Attendee(world.Student);

        Assert.False((await world.Session.PauseRecordingAsync(roomId, student)).Accepted);
        Assert.False((await world.Session.StopRecordingAsync(roomId, student)).Accepted);

        var snapshot = world.Session.GetRecordingSnapshot(roomId);
        Assert.Equal(RecordingState.Recording, snapshot.State);
        Assert.Equal(recordingId, snapshot.RecordingId);
        Assert.Equal(10_000, snapshot.ElapsedMs);
    }

    [Fact]
    public async Task Chuyen_trang_thai_khong_hop_le_bi_bo_qua_va_khong_doi_gi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var host = world.Attendee(world.Host);

        // Chưa ghi: không tạm dừng, tiếp tục hay dừng được.
        Assert.False((await world.Session.PauseRecordingAsync(roomId, host)).Accepted);
        Assert.False((await world.Session.ResumeRecordingAsync(roomId, host)).Accepted);
        Assert.False((await world.Session.StopRecordingAsync(roomId, host)).Accepted);

        var recordingId = await HostStartsRecording(world, roomId);
        world.Clock.AdvanceSeconds(7);

        // Đang ghi: không "tiếp tục", không bắt đầu thêm lần nữa.
        Assert.False((await world.Session.ResumeRecordingAsync(roomId, host)).Accepted);
        Assert.False((await world.Session.StartRecordingAsync(roomId, recordingId, host)).Accepted);

        var snapshot = world.Session.GetRecordingSnapshot(roomId);
        Assert.Equal(RecordingState.Recording, snapshot.State);
        Assert.Equal(7_000, snapshot.ElapsedMs);
    }

    [Fact]
    public async Task Khong_bat_dau_duoc_voi_ban_ghi_cua_buoi_hoc_khac()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom("Buổi A");
        var otherRoomId = world.AddLiveRoom("Buổi B");
        world.ActAs(world.Host);
        var otherRecording = ((OnlineClassRoomRecordingDto)(await world.Rooms.StartRecording(otherRoomId)).Value!).Id;

        var command = await world.Session.StartRecordingAsync(roomId, otherRecording, world.Attendee(world.Host));

        Assert.False(command.Accepted);
        Assert.Equal(RecordingState.Idle, world.Session.GetRecordingSnapshot(roomId).State);
    }

    [Fact]
    public async Task Moi_lan_chuyen_trang_thai_duoc_ghi_vao_dien_bien_kem_ban_ghi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var host = world.Attendee(world.Host);
        var recordingId = await HostStartsRecording(world, roomId);
        world.Clock.AdvanceSeconds(5);
        await world.Session.PauseRecordingAsync(roomId, host);
        world.Clock.AdvanceSeconds(5);
        await world.Session.ResumeRecordingAsync(roomId, host);
        world.Clock.AdvanceSeconds(5);
        await world.Session.StopRecordingAsync(roomId, host);

        var recordingEvents = world.EventsOf(roomId)
            .Where(e => e.EventType is OnlineClassRoomEventType.RecordingStarted or OnlineClassRoomEventType.RecordingPaused
                or OnlineClassRoomEventType.RecordingResumed or OnlineClassRoomEventType.RecordingStopped)
            .ToList();

        Assert.Equal(
            new[] { OnlineClassRoomEventType.RecordingStarted, OnlineClassRoomEventType.RecordingPaused,
                    OnlineClassRoomEventType.RecordingResumed, OnlineClassRoomEventType.RecordingStopped },
            recordingEvents.Select(e => e.EventType));
        Assert.All(recordingEvents, e => Assert.Equal(recordingId, e.RecordingId));
    }

    [Fact]
    public async Task Cac_gia_tri_loai_su_kien_cu_khong_bi_doi()
    {
        // Giá trị số nằm trong mọi dòng Diễn biến đã lưu: chỉ được thêm vào cuối, không bao giờ đánh số lại.
        Assert.Equal(0, (int)OnlineClassRoomEventType.Started);
        Assert.Equal(8, (int)OnlineClassRoomEventType.CamOff);
        Assert.Equal(9, (int)OnlineClassRoomEventType.RecordingStarted);
        Assert.Equal(10, (int)OnlineClassRoomEventType.RecordingPaused);
        Assert.Equal(11, (int)OnlineClassRoomEventType.RecordingResumed);
        Assert.Equal(12, (int)OnlineClassRoomEventType.RecordingStopped);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Ket_thuc_buoi_hoc_khi_dang_tam_dung_thi_chot_ban_ghi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var host = world.Attendee(world.Host);
        var recordingId = await HostStartsRecording(world, roomId);
        world.Clock.AdvanceSeconds(12);
        await world.Session.PauseRecordingAsync(roomId, host);
        world.Clock.AdvanceSeconds(60);

        Assert.True(await world.Session.EndByHostAsync(roomId, host));

        Assert.Equal(RecordingState.Idle, world.Session.GetRecordingSnapshot(roomId).State);
        var recording = Assert.Single(world.RecordingsOf(roomId));
        Assert.Equal(recordingId, recording.Id);
        Assert.NotNull(recording.StoppedAt);
        Assert.Equal(12_000, recording.DurationMs);
        Assert.Contains(world.EventsOf(roomId), e => e.EventType == OnlineClassRoomEventType.RecordingStopped && e.RecordingId == recordingId);
    }

    [Fact]
    public async Task Chu_tri_mat_ket_noi_khi_dang_ghi_thi_chot_ban_ghi_theo_dong_ho_server()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        await HostStartsRecording(world, roomId);
        world.Clock.AdvanceSeconds(33);

        await world.Session.EndAfterHostDisconnectedAsync(roomId, world.Attendee(world.Host));

        Assert.Equal(RecordingState.Idle, world.Session.GetRecordingSnapshot(roomId).State);
        var recording = Assert.Single(world.RecordingsOf(roomId));
        Assert.Equal(33_000, recording.DurationMs);
        Assert.NotNull(recording.StoppedAt);
    }

    [Fact]
    public async Task Nguoi_vao_muon_thay_dung_trang_thai_va_thoi_gian_sau_nhieu_lan_tam_dung()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var host = world.Attendee(world.Host);
        await HostStartsRecording(world, roomId);

        // 10 giây ghi, tạm dừng 20 giây, 5 giây ghi, tạm dừng 40 giây, 2 giây ghi: tổng thời gian ghi thật = 17 giây.
        world.Clock.AdvanceSeconds(10);
        await world.Session.PauseRecordingAsync(roomId, host);
        world.Clock.AdvanceSeconds(20);
        await world.Session.ResumeRecordingAsync(roomId, host);
        world.Clock.AdvanceSeconds(5);
        await world.Session.PauseRecordingAsync(roomId, host);
        world.Clock.AdvanceSeconds(40);
        await world.Session.ResumeRecordingAsync(roomId, host);
        world.Clock.AdvanceSeconds(2);

        // Học viên B vào phòng lúc này: nhận trạng thái hiện tại mà không cần chủ trì làm gì thêm.
        var lateJoiner = world.Session.GetRecordingSnapshot(roomId);

        Assert.Equal(RecordingState.Recording, lateJoiner.State);
        Assert.Equal(17_000, lateJoiner.ElapsedMs);
    }

    [Fact]
    public async Task Ket_thuc_buoi_hoc_xoa_trang_thai_va_khong_anh_huong_buoi_khac()
    {
        using var world = ClassRoomWorld.Create();
        var roomA = world.AddLiveRoom("Buổi A");
        var roomB = world.AddLiveRoom("Buổi B");
        await HostStartsRecording(world, roomA);
        await HostStartsRecording(world, roomB);
        world.Clock.AdvanceSeconds(9);

        await world.Session.EndByHostAsync(roomA, world.Attendee(world.Host));

        Assert.Equal(RecordingState.Idle, world.Session.GetRecordingSnapshot(roomA).State);
        var untouched = world.Session.GetRecordingSnapshot(roomB);
        Assert.Equal(RecordingState.Recording, untouched.State);
        Assert.Equal(9_000, untouched.ElapsedMs);
    }

    [Fact]
    public void Phong_chua_tung_ghi_hinh_co_trang_thai_khong_ghi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();

        var snapshot = world.Session.GetRecordingSnapshot(roomId);

        Assert.Equal(RecordingState.Idle, snapshot.State);
        Assert.Equal(0, snapshot.ElapsedMs);
        Assert.Null(snapshot.RecordingId);
    }

    [Fact]
    public async Task Ket_thuc_ghi_hinh_luu_thoi_luong_that_va_va_lai_tep_video()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddLiveRoom();
        var recordingId = await HostStartsRecording(world, roomId);

        var finished = await world.Rooms.FinishRecording(roomId, recordingId, new FinishRecordingRequest(20_000));

        Assert.True(finished.IsSuccess);
        var recording = Assert.Single(world.RecordingsOf(roomId));
        Assert.Equal(20_000, recording.DurationMs);
        Assert.NotNull(recording.StoppedAt);
        var patch = Assert.Single(world.Media.Patches);
        Assert.Equal(20_000, patch.DurationMs);
        Assert.EndsWith(".webm", patch.FilePath);
    }
}
