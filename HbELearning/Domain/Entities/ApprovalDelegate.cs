// File: Domain/Entities/ApprovalDelegate.cs
namespace HbELearning.Domain.Entities;

/// <summary>Nhân viên CÙNG PHÒNG BAN được Giám Đốc Nhân sự ủy quyền duyệt xuất bản nội dung khóa học ở bước cuối (thay vì
/// phải chính Giám Đốc Nhân sự bấm Duyệt/Từ chối) — áp dụng cho khóa học đang chờ Giám Đốc Nhân sự duyệt.</summary>
public class ApprovalDelegate
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int GrantedById { get; set; }
    public User GrantedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
