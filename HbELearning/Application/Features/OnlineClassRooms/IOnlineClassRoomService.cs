// File: Application/Features/OnlineClassRooms/IOnlineClassRoomService.cs
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace HbELearning.Application.Features.OnlineClassRooms;

/// <summary>Các ca sử dụng của chức năng OnlineClassRoom. Tầng API gọi qua cổng này (xem API/Controllers/OnlineClassRoomService.cs).</summary>
public interface IOnlineClassRoomService
{
    /// <summary>Danh sách phòng học online — mở cho toàn công ty xem, ai cũng tạo được phòng để trao đổi/họp nhanh.</summary>
    Task<Result> GetList(OnlineClassRoomStatus? status);

    Task<Result> GetById(int id);

    /// <summary>Tra id phòng học theo mã tham gia — dùng khi người tham gia chỉ có mã (không có liên kết kèm
    /// sẵn id). Chỉ tìm trong các phòng còn hoạt động (đã lên lịch hoặc đang diễn ra); việc kiểm tra mã có
    /// đúng để THỰC SỰ vào được phòng vẫn do ClassRoomHub.JoinRoom quyết định.</summary>
    Task<Result> ResolveByCode(string code);

    /// <summary>Nhật ký diễn biến (biên bản) của buổi học — vào/ra phòng, chat, bật/tắt mic-cam, bắt đầu/kết thúc. Ghi tự động từ ClassRoomHub trong lúc buổi học diễn ra.</summary>
    Task<Result> GetEvents(int id);

    /// <summary>Danh sách bản ghi video của buổi học (quay ở phía trình duyệt người chủ trì bằng MediaRecorder).</summary>
    Task<Result> GetRecordings(int id);

    /// <summary>Người chủ trì hoặc bất kỳ người tham gia nào bắt đầu
    /// 1 phiên ghi hình mới — tạo sẵn file rỗng trên đĩa và 1 dòng bản ghi trong DB, trả về id để gửi tiếp
    /// từng đoạn video nhỏ qua UploadRecordingChunk. Ghi theo từng đoạn nhỏ ngay trong lúc quay (thay vì gom
    /// hết rồi tải lên 1 lần lúc kết thúc) để nếu trình duyệt bị đóng/mất mạng đột ngột, phần đã ghi tới thời
    /// điểm đó vẫn được lưu thay vì mất trắng.</summary>
    Task<Result> StartRecording(int id);

    /// <summary>Nối thêm 1 đoạn video nhỏ (vài giây) vào file bản ghi đã tạo ở StartRecording. Các đoạn
    /// MediaRecorder khi ghép nối tiếp theo đúng thứ tự tạo thành 1 file webm hợp lệ, không cần xử lý gì thêm —
    /// nhưng client PHẢI gửi tuần tự (đợi đoạn trước xong mới gửi đoạn sau), nếu không file sẽ bị lẫn thứ tự.</summary>
    Task<Result> UploadRecordingChunk(int id, int recordingId, FileUpload file);

    /// <summary>Người chủ trì báo hiệu đã ghi xong, kèm thời lượng thật (đo bằng đồng hồ trình duyệt) — dùng
    /// để vá lại trường Duration trong header webm (xem WebmDurationFixer), nếu không trình duyệt phát lại sẽ
    /// báo thời lượng "Infinity" và thanh tua chạy bất thường. Vá thất bại không phải lỗi nghiêm trọng —
    /// video vẫn phát được bình thường, chỉ là không tua chính xác.</summary>
    Task<Result> FinishRecording(int id, int recordingId, FinishRecordingRequest request);

    /// <summary>
    /// Danh sách ICE server cho WebRTC. Nếu đã cấu hình server STUN/TURN tự triển khai (mục "Turn"
    /// trong appsettings), sinh kèm username/mật khẩu TURN có hạn dùng ngắn (chuẩn TURN REST API,
    /// theo thuật toán static-auth-secret của coturn) — không có server bên thứ ba nào tham gia.
    /// Chưa cấu hình thì trả về danh sách rỗng, WebRTC chỉ dùng được trong cùng mạng LAN/VPN.
    /// </summary>
    Result GetIceServers();

    /// <summary>Mọi vai trò trừ Admin đều tạo được phòng học online — Admin chỉ quản trị hệ thống, không trực tiếp tổ chức buổi học.</summary>
    Task<Result> Create(OnlineClassRoomRequest request);

    /// <summary>Admin sửa được mọi phòng (quản trị); các vai trò khác chỉ sửa được phòng do chính mình tạo.</summary>
    Task<Result> Update(int id, OnlineClassRoomRequest request);

    /// <summary>Hủy buổi học đã lên lịch (chưa diễn ra). Admin hủy được mọi phòng; các vai trò khác chỉ hủy được phòng do chính mình tạo.</summary>
    Task<Result> Cancel(int id);

    Task<Result> Delete(int id);
}
