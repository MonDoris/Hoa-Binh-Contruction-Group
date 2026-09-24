// File: Application/Features/ApprovalWorkflows/IApprovalWorkflowService.cs

namespace HbELearning.Application.Features.ApprovalWorkflows;

/// <summary>Các ca sử dụng của chức năng ApprovalWorkflow. Tầng API gọi qua cổng này (xem API/Controllers/ApprovalWorkflowService.cs).</summary>
public interface IApprovalWorkflowService
{
    /// <summary>CẤP PHÒNG BAN — luồng cấp duyệt của chính phòng ban người đăng nhập, kèm các bước.
    ///
    /// Admin đứng ngoài mọi phòng ban nên nhận về TOÀN BỘ luồng, lọc theo <paramref name="departmentId"/>
    /// (một phòng ban cụ thể) hoặc <paramref name="sharedOnly"/> (chỉ luồng dùng chung) nếu muốn. Trưởng
    /// Nhóm / Trưởng phòng / Giám Đốc thì hai tham số lọc bị BỎ QUA: họ luôn chỉ thấy đúng phòng ban của
    /// mình — muốn xem của phòng khác thì sang <see cref="GetCompanyList"/>.</summary>
    Task<Result> GetList(bool activeOnly = false, int? departmentId = null, bool sharedOnly = false);

    /// <summary>CẤP CÔNG TY — luồng cấp duyệt của các phòng ban KHÁC, để Trưởng phòng / Giám Đốc tham khảo
    /// cách phòng bạn dựng luồng rồi <see cref="Clone"/> về phòng mình.
    ///
    /// Chỉ để ĐỌC: không có Sửa/Xóa ở đây, và chính phòng ban của người xem bị loại khỏi danh sách — luồng
    /// của phòng mình đã nằm trọn ở bảng cấp phòng ban rồi, lặp lại chỉ khiến họ tưởng đó là hai luồng khác
    /// nhau. Admin gọi thì thấy tất cả, không loại gì.</summary>
    Task<Result> GetCompanyList(int? departmentId = null, bool sharedOnly = false);

    /// <summary>Các vai trò còn hoạt động để gán cho một bước duyệt.
    ///
    /// Có API /api/role rồi nhưng nó chỉ Admin gọi được (và trả kèm bộ quyền của từng vai trò, thứ không
    /// liên quan gì tới việc dựng luồng). Mở /api/role cho cả ba cấp phòng ban chỉ vì một ô xổ xuống là
    /// nới quá tay — trả đúng id + tên ở đây gọn và kín hơn.</summary>
    Task<Result> GetRoleOptionList();

    /// <summary>Những người có thể ghim làm NGƯỜI DUYỆT CỐ ĐỊNH của một bước: còn hoạt động, giữ đúng vai trò
    /// của bước (vai trò con cũng tính — "Trưởng phòng IT" khớp bước giao cho "Trưởng phòng", đúng luật
    /// CourseService.CanActOnStep), và thuộc đúng phòng ban của bước.
    ///
    /// Bỏ trống <paramref name="departmentId"/> = không lọc phòng ban (bước để "cùng phòng ban với người tạo"
    /// trên một luồng dùng chung của Admin); với người cấp phòng ban thì bỏ trống có nghĩa là phòng ban của
    /// chính họ, chứ không phải cả công ty.
    ///
    /// Gửi kèm một phòng ban KHÁC vẫn được — một bước hoàn toàn có thể giao cho phòng khác (VD bước cuối luôn
    /// do phòng Nhân sự duyệt), và ValidateFixedUserAsync cũng chấp nhận đúng như vậy khi lưu. Ép cứng về
    /// phòng ban của người đăng nhập ở đây sẽ khiến ô chọn của những bước đó trống trơn không rõ lý do.</summary>
    Task<Result> GetApproverOptionList(int roleId, int? departmentId = null);

    /// <summary>Các luồng ĐANG HOẠT ĐỘNG để đổ vào ô xổ xuống "Luồng cấp duyệt" trên biểu mẫu khóa học.
    /// Mở cho cả người soạn nội dung — họ là người chọn luồng, nhưng không được sửa định nghĩa luồng.
    ///
    /// Chỉ trả luồng của CHÍNH phòng ban người soạn cộng với luồng dùng chung: luồng của phòng khác ghim sẵn
    /// người duyệt của phòng đó, chọn nhầm là bài đi lạc sang cấp trên của một phòng không liên quan. Admin
    /// thấy tất cả — họ dựng luồng cho mọi phòng nên cần xem được toàn bộ.</summary>
    Task<Result> GetOptionList();

    /// <summary>Tạo luồng. Cấp phòng ban dựng luồng cho CHÍNH phòng ban của mình — DepartmentId lấy từ
    /// token, bỏ qua giá trị gửi lên; chỉ Admin mới đặt luồng sang phòng khác hoặc để "Dùng chung".</summary>
    Task<Result> Create(ApprovalWorkflowRequest request);

    /// <summary>Sửa luồng. Trong một phòng ban, cả ba cấp (Trưởng Nhóm, Trưởng phòng, Giám Đốc) đều sửa
    /// được mọi luồng của phòng đó — luồng là tài sản của phòng ban chứ không của người bấm nút tạo. Luồng
    /// của phòng KHÁC và luồng dùng chung thì chỉ Admin đụng tới được.</summary>
    Task<Result> Update(int id, ApprovalWorkflowRequest request);

    /// <summary>Nhân bản một luồng sang phòng ban khác — thấy luồng của phòng bạn hay thì lấy nguyên hình
    /// dạng của nó (số bước, thứ tự, vai trò từng bước) về áp dụng cho phòng mình, khỏi dựng lại từ đầu.
    ///
    /// KHÔNG chép người duyệt đã ghim: người đó là nhân sự của phòng gốc, sang phòng khác là sai cấp duyệt.
    /// Những bước đó hạ về "cả nhóm giữ vai trò" để bản sao vẫn chạy được ngay, và bản sao được tạo ở trạng
    /// thái NGỪNG HOẠT ĐỘNG để người nhân bản buộc phải mở ra chọn người duyệt của phòng mình trước khi dùng.
    ///
    /// Phạm vi phòng ban của TỪNG BƯỚC cũng đổi theo: bước nào đang trỏ đúng phòng ban của luồng gốc thì
    /// chuyển sang phòng ban đích; bước trỏ phòng ban khác (VD bước cuối luôn do phòng Nhân sự duyệt) giữ
    /// nguyên, và bước để trống ("cùng phòng ban với người tạo") vẫn để trống.</summary>
    Task<Result> Clone(int id, CloneApprovalWorkflowRequest request);

    Task<Result> Delete(int id);
}
