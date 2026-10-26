// File: Application/Features/Chat/ChatService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Chat;

/// <summary>Nhắn tin nội bộ giữa các tài khoản: cuộc 1-1, nhóm, và đính kèm (ảnh / tệp / thư mục / nhãn
/// dán / ghi âm).
///
/// CỐ Ý không khai vào FunctionKey.ModuleByController: mọi tài khoản đã đăng nhập đều được nhắn tin,
/// giống AiTutorService. Khai vào đó sẽ khiến việc gửi tin đòi quyền "chat.create" — một khóa không
/// tồn tại — và chặn sạch mọi vai trò do Admin tự tạo.</summary>
public class ChatService : IChatService
{
    private const int PageSize = 30;

    /// <summary>Trần cỡ một tệp đính kèm trong chat. Thấp hơn hẳn trần chung của Upload:MaxFileSizeMb (2 GB,
    /// đặt cho video bài học): hộp chat gửi kèm được cả THƯ MỤC, nên một thao tác có thể là hàng chục tệp một
    /// lúc, và không ai chờ nổi một lượt gửi hàng trăm MB trong cái bảng rộng 340 pixel.</summary>
    public const long MaxAttachmentByte = 50L * 1024 * 1024;

    /// <summary>Thư mục lưu đính kèm chat, nằm trong wwwroot/uploads.</summary>
    private const string AttachmentFolder = "chat";

    /// <summary>Đuôi tệp chấp nhận cho ẢNH NHÓM. Hẹp hơn hẳn danh sách chung của FileService: chỗ này đổ thẳng
    /// vào một thẻ &lt;img&gt;, mà một tệp .pdf hay .mp4 đặt làm ảnh nhóm thì chỉ ra một ô trống vĩnh viễn, không
    /// báo lỗi gì. Cố ý không nhận .svg: ảnh SVG là một tài liệu chứa script được, mà nó lại nằm cùng gốc với
    /// ứng dụng nên ai cũng tải lên được một trang chạy mã trên chính tên miền này.</summary>
    private static readonly string[] ImageExtensionList = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>Những mẫu nền khung chat được phép đặt. Phải khớp đúng bộ [data-bg] trong css/style.css.
    ///
    /// Lọc ở đây chứ không chỉ ở trình duyệt: giá trị này đi thẳng vào một thuộc tính HTML lúc vẽ, nên một chuỗi
    /// tự do gửi lên bằng tay là một lỗ hổng chờ sẵn — và một khóa không có thật thì chỉ ra một khung chat không
    /// nền, người dùng không hiểu tại sao đổi mà không thấy gì.</summary>
    private static readonly HashSet<string> BackgroundKeySet = new(StringComparer.OrdinalIgnoreCase)
    { "sky", "mint", "peach", "lilac", "rose", "sand", "slate", "forest" };

    private readonly IAppDbContext _db;
    private readonly IChatNotifier _chatNotifier;
    private readonly IFileService _fileService;
    private readonly ICurrentUser _currentUser;

    public ChatService(IAppDbContext db, IChatNotifier chatNotifier, IFileService fileService, ICurrentUser currentUser)
    {
        _db = db;
        _chatNotifier = chatNotifier;
        _fileService = fileService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetConversationList()
    {
        // Gán ra biến cục bộ: CurrentUserId là property đọc từ ClaimsPrincipal, EF không dịch được nó
        // thành SQL khi nó nằm trong cây biểu thức của Where/Select.
        var userId = _currentUser.UserId;

        var raw = await _db.Conversation.AsNoTracking()
            .Where(x => x.MemberList.Any(m => m.UserId == userId))
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.Title,
                x.LastMessageAt,
                x.CreatedByUserId,
                x.AvatarUrl,
                MemberCount = x.MemberList.Count,
                // Nền là của RIÊNG người đang hỏi, nên đọc từ chính dòng thành viên của họ chứ không phải từ hội thoại.
                MyBackgroundKey = x.MemberList.Where(m => m.UserId == userId)
                    .Select(m => m.BackgroundKey).FirstOrDefault(),
                MyBackgroundUrl = x.MemberList.Where(m => m.UserId == userId)
                    .Select(m => m.BackgroundUrl).FirstOrDefault(),
                Partner = x.MemberList.Where(m => m.UserId != userId)
                    .Select(m => new { m.User.Id, m.User.FullName, m.User.AvatarUrl })
                    .FirstOrDefault(),
                LastMessage = x.MessageList.OrderByDescending(m => m.Id)
                    .Select(m => new { m.Content, m.SenderUserId, m.IsDeleted, m.MessageType, m.AttachmentName })
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Số chưa đọc của TẤT CẢ hội thoại trong một câu truy vấn: đếm tin có Id lớn hơn mốc đã đọc của
        // chính người này. Gọi riêng cho từng hội thoại thì mở hộp chat là bắn ra hàng chục request.
        var unreadList = await (
            from m in _db.ChatMessage.AsNoTracking()
            join me in _db.ConversationMember.AsNoTracking() on m.ConversationId equals me.ConversationId
            where me.UserId == userId
                  && m.SenderUserId != userId
                  && !m.IsDeleted
                  && m.Id > (me.LastReadMessageId ?? 0)
            group m by m.ConversationId into g
            select new { ConversationId = g.Key, Count = g.Count() })
            .ToListAsync();

        var unreadMap = unreadList.ToDictionary(x => x.ConversationId, x => x.Count);

        var list = raw
            .Select(x => new ConversationDto(
                x.Id,
                x.Type.ToString(),
                x.Type == ConversationType.Direct ? (x.Partner?.FullName ?? "Người dùng") : (x.Title ?? "Nhóm"),
                x.Type == ConversationType.Direct ? x.Partner?.AvatarUrl : x.AvatarUrl,
                x.Type == ConversationType.Direct ? x.Partner?.Id : null,
                x.MemberCount,
                x.LastMessage is null
                    ? null
                    : (x.LastMessage.IsDeleted
                        ? "Tin nhắn đã thu hồi"
                        : PreviewOf(x.LastMessage.MessageType, x.LastMessage.Content, x.LastMessage.AttachmentName)),
                x.LastMessage is not null && x.LastMessage.SenderUserId == userId,
                x.LastMessageAt,
                unreadMap.GetValueOrDefault(x.Id),
                x.CreatedByUserId,
                x.MyBackgroundKey,
                x.MyBackgroundUrl))
            // Hội thoại chưa có tin nào (vừa bấm "nhắn tin" mà chưa gõ gì) vẫn phải nằm trên cùng,
            // nếu không người dùng bấm xong rồi không tìm lại được nó ở đâu.
            .OrderByDescending(x => x.LastMessageAt ?? DateTime.MaxValue)
            .ToList();

        return Result.Success(list);
    }

    public async Task<Result> GetUnreadCount()
    {
        var userId = _currentUser.UserId;

        var count = await (
            from m in _db.ChatMessage.AsNoTracking()
            join me in _db.ConversationMember.AsNoTracking() on m.ConversationId equals me.ConversationId
            where me.UserId == userId && m.SenderUserId != userId
                  && !m.IsDeleted && m.Id > (me.LastReadMessageId ?? 0)
            select m.Id).CountAsync();

        return Result.Success(new UnreadCountDto(count));
    }

    public async Task<Result> OpenDirect(int userId)
    {
        if (userId == _currentUser.UserId) return Result.Invalid(new MessageResponse("Không thể tự nhắn tin cho chính mình."));

        var partner = await _db.User.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);
        if (partner is null) return Result.NotFound(new MessageResponse("Không tìm thấy tài khoản này."));

        var directKey = DirectKeyOf(_currentUser.UserId, userId);
        var conversation = await _db.Conversation.FirstOrDefaultAsync(x => x.DirectKey == directKey);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                Type = ConversationType.Direct,
                DirectKey = directKey,
                CreatedByUserId = _currentUser.UserId,
                MemberList =
                {
                    new ConversationMember { UserId = _currentUser.UserId },
                    new ConversationMember { UserId = userId }
                }
            };
            _db.Conversation.Add(conversation);

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Người kia vừa bấm "nhắn tin" trước ta một nhịp — chỉ mục duy nhất trên DirectKey đã chặn
                // bản sao. Lấy lại cái họ vừa tạo thay vì trả lỗi ra màn hình.
                _db.ChangeTracker.Clear();
                conversation = await _db.Conversation.FirstAsync(x => x.DirectKey == directKey);
            }
        }

        // Đọc SAU khi đã chắc chắn có hội thoại: cuộc vừa tạo thì chưa đặt nền bao giờ, còn cuộc cũ mở lại
        // thì phải giữ đúng nền người ta đã chọn, không bật lại về mặc định mỗi lần bấm "nhắn tin".
        var background = await _db.ConversationMember.AsNoTracking()
            .Where(x => x.ConversationId == conversation.Id && x.UserId == _currentUser.UserId)
            .Select(x => new { x.BackgroundKey, x.BackgroundUrl }).FirstOrDefaultAsync();

        return Result.Success(new ConversationDto(
            conversation.Id, ConversationType.Direct.ToString(), partner.FullName, partner.AvatarUrl,
            partner.Id, 2, null, false, conversation.LastMessageAt, 0, conversation.CreatedByUserId,
            background?.BackgroundKey, background?.BackgroundUrl));
    }

    public async Task<Result> CreateGroup(CreateGroupRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return Result.Invalid(new MessageResponse("Nhóm chưa có tên."));
        if (title.Length > 200) return Result.Invalid(new MessageResponse("Tên nhóm quá dài (tối đa 200 ký tự)."));

        // Lọc trùng và bỏ chính mình TRƯỚC khi kiểm số lượng: người dùng tick trùng hoặc tick cả bản thân thì
        // đó vẫn là một nhóm hợp lệ, không phải lỗi để quăng ra màn hình.
        var memberIdList = (request.MemberIdList ?? new List<int>())
            .Where(x => x != _currentUser.UserId).Distinct().ToList();
        if (memberIdList.Count == 0)
            return Result.Invalid(new MessageResponse("Chọn ít nhất một người để lập nhóm."));

        var validIdList = await _db.User.AsNoTracking()
            .Where(x => memberIdList.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync();
        if (validIdList.Count == 0)
            return Result.Invalid(new MessageResponse("Không có tài khoản nào hợp lệ trong danh sách đã chọn."));

        var conversation = new Conversation
        {
            Type = ConversationType.Group,
            Title = title,
            DirectKey = null,          // chỉ mục duy nhất trên cột này đã lọc bỏ NULL nên mọi nhóm cùng để trống
            CreatedByUserId = _currentUser.UserId
        };
        conversation.MemberList.Add(new ConversationMember { UserId = _currentUser.UserId });
        foreach (var id in validIdList) conversation.MemberList.Add(new ConversationMember { UserId = id });

        _db.Conversation.Add(conversation);
        await _db.SaveChangesAsync();

        await NotifyConversationChangedAsync(conversation.Id);

        return Result.Success(new ConversationDto(
            conversation.Id, ConversationType.Group.ToString(), title, null, null, validIdList.Count + 1,
            null, false, null, 0, _currentUser.UserId, null, null));
    }

    public async Task<Result> RenameGroup(int id, RenameGroupRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return Result.Invalid(new MessageResponse("Nhóm chưa có tên."));
        if (title.Length > 200) return Result.Invalid(new MessageResponse("Tên nhóm quá dài (tối đa 200 ký tự)."));

        var conversation = await _db.Conversation.FirstOrDefaultAsync(x => x.Id == id);
        if (conversation is null || conversation.Type != ConversationType.Group)
            return Result.NotFound(new MessageResponse("Không tìm thấy nhóm."));
        if (conversation.CreatedByUserId != _currentUser.UserId && !_currentUser.IsAdmin)
            return Result.Invalid(new MessageResponse("Chỉ người lập nhóm mới được đổi tên nhóm."));

        conversation.Title = title;
        await _db.SaveChangesAsync();

        await NotifyConversationChangedAsync(id);
        return Result.Success(new MessageResponse("Đã đổi tên nhóm."));
    }

    public async Task<Result> ChangeGroupAvatar(int id, FileUpload file)
    {
        var conversation = await _db.Conversation.FirstOrDefaultAsync(x => x.Id == id);
        if (conversation is null || conversation.Type != ConversationType.Group)
            return Result.NotFound(new MessageResponse("Không tìm thấy nhóm."));
        if (conversation.CreatedByUserId != _currentUser.UserId && !_currentUser.IsAdmin)
            return Result.Invalid(new MessageResponse("Chỉ người lập nhóm mới được đổi ảnh nhóm."));

        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn ảnh."));
        if (file.Length > 10 * 1024 * 1024)
            return Result.Invalid(new MessageResponse("Ảnh nhóm tối đa 10 MB."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ImageExtensionList.Contains(extension))
            return Result.Invalid(new MessageResponse("Chỉ nhận tệp ảnh (png, jpg, gif, webp, bmp)."));

        var oldUrl = conversation.AvatarUrl;

        UploadResultDto result;
        try { result = await _fileService.SaveAsync(file, AttachmentFolder); }
        catch (ArgumentException ex) { return Result.Invalid(new MessageResponse(ex.Message)); }

        conversation.AvatarUrl = result.Url;
        await _db.SaveChangesAsync();

        // Dọn ảnh cũ SAU khi đã lưu được ảnh mới. Xóa trước thì lượt lưu hỏng giữa chừng sẽ để nhóm trơ
        // ra không còn ảnh nào — mất cả cái đang có lẫn cái định thay.
        if (!string.IsNullOrEmpty(oldUrl)) _fileService.Delete(oldUrl);

        await NotifyConversationChangedAsync(id);

        var memberCount = await _db.ConversationMember.AsNoTracking().CountAsync(x => x.ConversationId == id);
        var background = await _db.ConversationMember.AsNoTracking()
            .Where(x => x.ConversationId == id && x.UserId == _currentUser.UserId)
            .Select(x => new { x.BackgroundKey, x.BackgroundUrl }).FirstOrDefaultAsync();

        return Result.Success(new ConversationDto(
            id, ConversationType.Group.ToString(), conversation.Title ?? "Nhóm", conversation.AvatarUrl,
            null, memberCount, null, false, conversation.LastMessageAt, 0,
            conversation.CreatedByUserId, background?.BackgroundKey, background?.BackgroundUrl));
    }

    public async Task<Result> RemoveGroupAvatar(int id)
    {
        var conversation = await _db.Conversation.FirstOrDefaultAsync(x => x.Id == id);
        if (conversation is null || conversation.Type != ConversationType.Group)
            return Result.NotFound(new MessageResponse("Không tìm thấy nhóm."));
        if (conversation.CreatedByUserId != _currentUser.UserId && !_currentUser.IsAdmin)
            return Result.Invalid(new MessageResponse("Chỉ người lập nhóm mới được đổi ảnh nhóm."));

        if (!string.IsNullOrEmpty(conversation.AvatarUrl)) _fileService.Delete(conversation.AvatarUrl);
        conversation.AvatarUrl = null;
        await _db.SaveChangesAsync();

        await NotifyConversationChangedAsync(id);
        return Result.Success(new MessageResponse("Đã gỡ ảnh nhóm."));
    }

    public async Task<Result> SetBackground(int id, SetBackgroundRequest request)
    {
        var member = await _db.ConversationMember
            .FirstOrDefaultAsync(x => x.ConversationId == id && x.UserId == _currentUser.UserId);
        if (member is null) return Result.Forbidden();

        var key = request.BackgroundKey?.Trim();

        // "default" và chuỗi rỗng cùng nghĩa là bỏ nền — lưu null để chỉ có MỘT cách biểu diễn "không nền".
        if (string.IsNullOrEmpty(key) || key.Equals("default", StringComparison.OrdinalIgnoreCase))
            member.BackgroundKey = null;
        else if (BackgroundKeySet.Contains(key))
            member.BackgroundKey = key.ToLowerInvariant();
        else
            return Result.Invalid(new MessageResponse("Mẫu nền không hợp lệ."));

        // Chọn một mẫu dựng sẵn thì bỏ luôn ảnh tự tải lên trước đó — hai thứ không bao giờ cùng có mặt.
        // Giữ lại thì tệp nằm trên đĩa vĩnh viễn mà không còn đường nào dẫn tới nó để xóa.
        if (!string.IsNullOrEmpty(member.BackgroundUrl))
        {
            _fileService.Delete(member.BackgroundUrl);
            member.BackgroundUrl = null;
        }

        await _db.SaveChangesAsync();

        // Chỉ báo cho CHÍNH mình: nền là lựa chọn cá nhân, nhưng người ta có thể đang mở thêm tab khác
        // hoặc điện thoại, và những chỗ đó cũng phải đổi theo ngay.
        await _chatNotifier.NotifyConversationChangedAsync(_currentUser.UserId, id);
        return Result.Success(new MessageResponse("Đã đổi nền khung chat."));
    }

    public async Task<Result> GetMemberList(int id)
    {
        if (!await IsMemberAsync(id)) return Result.Forbidden();

        var ownerId = await _db.Conversation.AsNoTracking()
            .Where(x => x.Id == id).Select(x => x.CreatedByUserId).FirstOrDefaultAsync();

        var list = await _db.ConversationMember.AsNoTracking()
            .Where(x => x.ConversationId == id)
            .OrderBy(x => x.JoinedAt)
            .Select(x => new GroupMemberDto(
                x.UserId, x.User.FullName, x.User.AvatarUrl,
                x.User.Department != null ? x.User.Department.Name : null,
                x.UserId == ownerId, x.JoinedAt))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> AddMember(int id, AddGroupMemberRequest request)
    {
        var conversation = await _db.Conversation.FirstOrDefaultAsync(x => x.Id == id);
        if (conversation is null || conversation.Type != ConversationType.Group)
            return Result.NotFound(new MessageResponse("Không tìm thấy nhóm."));
        if (conversation.CreatedByUserId != _currentUser.UserId && !_currentUser.IsAdmin)
            return Result.Invalid(new MessageResponse("Chỉ người lập nhóm mới được mời thêm thành viên."));

        var currentIdList = await _db.ConversationMember.AsNoTracking()
            .Where(x => x.ConversationId == id).Select(x => x.UserId).ToListAsync();

        var newIdList = (request.MemberIdList ?? new List<int>())
            .Distinct().Where(x => !currentIdList.Contains(x)).ToList();
        if (newIdList.Count == 0) return Result.Success(new MessageResponse("Những người này đã ở trong nhóm."));

        var validIdList = await _db.User.AsNoTracking()
            .Where(x => newIdList.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync();
        if (validIdList.Count == 0)
            return Result.Invalid(new MessageResponse("Không có tài khoản nào hợp lệ trong danh sách đã chọn."));

        foreach (var userId in validIdList)
            _db.ConversationMember.Add(new ConversationMember { ConversationId = id, UserId = userId });
        await _db.SaveChangesAsync();

        await NotifyConversationChangedAsync(id);
        return Result.Success(new MessageResponse($"Đã thêm {validIdList.Count} thành viên vào nhóm."));
    }

    public async Task<Result> RemoveMember(int id, int userId)
    {
        var conversation = await _db.Conversation.FirstOrDefaultAsync(x => x.Id == id);
        if (conversation is null || conversation.Type != ConversationType.Group)
            return Result.NotFound(new MessageResponse("Không tìm thấy nhóm."));
        if (conversation.CreatedByUserId != _currentUser.UserId && !_currentUser.IsAdmin)
            return Result.Invalid(new MessageResponse("Chỉ người lập nhóm mới được mời thành viên ra."));
        if (userId == conversation.CreatedByUserId)
            return Result.Invalid(new MessageResponse("Không thể mời người lập nhóm ra khỏi nhóm."));

        var member = await _db.ConversationMember
            .FirstOrDefaultAsync(x => x.ConversationId == id && x.UserId == userId);
        if (member is null) return Result.NotFound(new MessageResponse("Người này không ở trong nhóm."));

        _db.ConversationMember.Remove(member);
        await _db.SaveChangesAsync();

        // Báo riêng cho người vừa bị mời ra: họ không còn nằm trong danh sách thành viên nữa nên
        // NotifyConversationChangedAsync không còn cách nào tìm ra họ, và hộp chat của họ sẽ treo lại một
        // nhóm đã rời cho tới tận lần tải trang kế tiếp.
        await _chatNotifier.NotifyConversationChangedAsync(userId, id);
        await NotifyConversationChangedAsync(id);
        return Result.Success(new MessageResponse("Đã mời thành viên ra khỏi nhóm."));
    }

    public async Task<Result> LeaveGroup(int id)
    {
        var conversation = await _db.Conversation.FirstOrDefaultAsync(x => x.Id == id);
        if (conversation is null || conversation.Type != ConversationType.Group)
            return Result.NotFound(new MessageResponse("Không tìm thấy nhóm."));

        var member = await _db.ConversationMember
            .FirstOrDefaultAsync(x => x.ConversationId == id && x.UserId == _currentUser.UserId);
        if (member is null) return Result.NotFound(new MessageResponse("Bạn không ở trong nhóm này."));

        // Ảnh nền riêng của người này chỉ được dòng thành viên này trỏ tới — xóa dòng trước là tệp nằm
        // lại trên đĩa vĩnh viễn mà không còn ai biết nó thuộc về đâu.
        if (!string.IsNullOrEmpty(member.BackgroundUrl)) _fileService.Delete(member.BackgroundUrl);

        _db.ConversationMember.Remove(member);
        await _db.SaveChangesAsync();

        var remainingList = await _db.ConversationMember
            .Where(x => x.ConversationId == id).OrderBy(x => x.JoinedAt).ToListAsync();

        if (remainingList.Count == 0)
        {
            // Nhóm rỗng thì dọn luôn cả tin nhắn: để lại thì không còn ai vào xem được nữa mà dữ liệu vẫn nằm
            // đó vĩnh viễn. Xóa tin TRƯỚC vì khóa ngoại ChatMessage -> Conversation đặt NoAction, cơ sở dữ
            // liệu sẽ không tự dọn hộ.
            var messageList = await _db.ChatMessage.Where(x => x.ConversationId == id).ToListAsync();
            foreach (var m in messageList.Where(x => !string.IsNullOrEmpty(x.AttachmentUrl)))
                _fileService.Delete(m.AttachmentUrl!);
            _db.ChatMessage.RemoveRange(messageList);
            _db.Conversation.Remove(conversation);
            await _db.SaveChangesAsync();

            await _chatNotifier.NotifyConversationChangedAsync(_currentUser.UserId, id);
            return Result.Success(new MessageResponse("Đã rời nhóm."));
        }

        // Người lập nhóm rời đi thì nhóm mất chủ: không còn ai đổi tên hay mời người vào được nữa. Trao lại
        // cho người vào sớm nhất trong số còn ở lại thay vì để nhóm đóng băng.
        if (conversation.CreatedByUserId == _currentUser.UserId)
        {
            conversation.CreatedByUserId = remainingList[0].UserId;
            await _db.SaveChangesAsync();
        }

        await _chatNotifier.NotifyConversationChangedAsync(_currentUser.UserId, id);
        await NotifyConversationChangedAsync(id);
        return Result.Success(new MessageResponse("Đã rời nhóm."));
    }

    public async Task<Result> SetBackgroundImage(int id, FileUpload file)
    {
        var member = await _db.ConversationMember
            .FirstOrDefaultAsync(x => x.ConversationId == id && x.UserId == _currentUser.UserId);
        if (member is null) return Result.Forbidden();

        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn ảnh."));
        if (file.Length > 10 * 1024 * 1024)
            return Result.Invalid(new MessageResponse("Ảnh nền tối đa 10 MB."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ImageExtensionList.Contains(extension))
            return Result.Invalid(new MessageResponse("Chỉ nhận tệp ảnh (png, jpg, gif, webp, bmp)."));

        var oldUrl = member.BackgroundUrl;

        UploadResultDto result;
        try { result = await _fileService.SaveAsync(file, AttachmentFolder); }
        catch (ArgumentException ex) { return Result.Invalid(new MessageResponse(ex.Message)); }

        member.BackgroundUrl = result.Url;
        member.BackgroundKey = null;        // ảnh thắng mẫu dựng sẵn, không để cả hai cùng có giá trị
        await _db.SaveChangesAsync();

        // Dọn ảnh cũ SAU khi ảnh mới đã lưu được: xóa trước thì lượt lưu hỏng giữa chừng làm mất cả hai.
        if (!string.IsNullOrEmpty(oldUrl)) _fileService.Delete(oldUrl);

        await _chatNotifier.NotifyConversationChangedAsync(_currentUser.UserId, id);
        return Result.Success(result);
    }

    public async Task<Result> GetMessageList(int id, int? beforeId)
    {
        if (!await IsMemberAsync(id)) return Result.Forbidden();

        var query = _db.ChatMessage.AsNoTracking().Where(x => x.ConversationId == id);
        if (beforeId is > 0) query = query.Where(x => x.Id < beforeId);

        var list = await query
            .OrderByDescending(x => x.Id)
            .Take(PageSize)
            .Select(x => new ChatMessageDto(
                x.Id, x.ConversationId, x.SenderUserId, x.SenderUser.FullName, x.SenderUser.AvatarUrl,
                x.IsDeleted ? string.Empty : x.Content,
                x.IsDeleted ? null : x.AttachmentUrl,
                x.IsDeleted, x.CreatedAt,
                x.MessageType.ToString(),
                x.IsDeleted ? null : x.AttachmentName,
                x.IsDeleted ? 0 : x.AttachmentSize))
            .ToListAsync();

        list.Reverse();   // trả theo thứ tự thời gian để giao diện chỉ việc nối vào cuối
        return Result.Success(list);
    }

    public async Task<Result> SendMessage(int id, SendMessageRequest request)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        var messageType = ParseMessageType(request.MessageType);

        if (content.Length == 0 && string.IsNullOrEmpty(request.AttachmentUrl))
            return Result.Invalid(new MessageResponse("Nội dung tin nhắn đang để trống."));
        if (content.Length > 4000)
            return Result.Invalid(new MessageResponse("Tin nhắn quá dài (tối đa 4000 ký tự)."));
        if (messageType is ChatMessageType.Image or ChatMessageType.File or ChatMessageType.Audio
            && string.IsNullOrEmpty(request.AttachmentUrl))
            return Result.Invalid(new MessageResponse("Tin nhắn kiểu này phải có tệp đính kèm."));

        // Chỉ nhận đường dẫn do CHÍNH endpoint tải lên ở dưới sinh ra. Bỏ kiểm tra này thì AttachmentUrl
        // thành một ô nhập tự do: gửi "/appsettings.json" là bên nhận bấm một cái tải được cấu hình máy chủ
        // (kèm chuỗi kết nối và mật khẩu SMTP), còn gửi "https://..." thì hộp chat nội bộ thành chỗ phát tán
        // liên kết ra ngoài.
        if (!string.IsNullOrEmpty(request.AttachmentUrl)
            && !request.AttachmentUrl.StartsWith($"/uploads/{AttachmentFolder}/", StringComparison.Ordinal))
            return Result.Invalid(new MessageResponse("Tệp đính kèm không hợp lệ."));

        var memberIdList = await _db.ConversationMember.AsNoTracking()
            .Where(x => x.ConversationId == id).Select(x => x.UserId).ToListAsync();
        if (!memberIdList.Contains(_currentUser.UserId)) return Result.Forbidden();

        var message = new ChatMessage
        {
            ConversationId = id,
            SenderUserId = _currentUser.UserId,
            Content = content,
            MessageType = messageType,
            AttachmentUrl = request.AttachmentUrl,
            // Cắt cho vừa cột: đường dẫn tương đối của một tệp nằm sâu trong thư mục dễ vượt 300 ký tự, mà
            // một tin nhắn không gửi được chỉ vì cái TÊN thì khó hiểu hơn hẳn một cái tên bị rút ngắn. Giữ
            // phần ĐUÔI vì tên tệp nằm ở đó, phần đầu chỉ là các cấp thư mục.
            AttachmentName = request.AttachmentName is { Length: > 300 } longName
                ? longName[^300..]
                : request.AttachmentName,
            AttachmentSize = request.AttachmentSize
        };
        _db.ChatMessage.Add(message);

        var conversation = await _db.Conversation.FirstAsync(x => x.Id == id);
        conversation.LastMessageAt = message.CreatedAt;

        // Người gửi đương nhiên đã đọc tin của chính mình — không cập nhật thì số chưa đọc của họ tự tăng.
        var me = await _db.ConversationMember.FirstAsync(x => x.ConversationId == id && x.UserId == _currentUser.UserId);

        await _db.SaveChangesAsync();
        me.LastReadMessageId = message.Id;      // gán SAU khi lưu, lúc này Id mới có giá trị thật
        await _db.SaveChangesAsync();

        var sender = await _db.User.AsNoTracking()
            .Where(x => x.Id == _currentUser.UserId)
            .Select(x => new { x.FullName, x.AvatarUrl }).FirstAsync();

        var dto = new ChatMessageDto(message.Id, id, _currentUser.UserId, sender.FullName, sender.AvatarUrl,
            message.Content, message.AttachmentUrl, false, message.CreatedAt,
            message.MessageType.ToString(), message.AttachmentName, message.AttachmentSize);

        // Đẩy cho MỌI thành viên, kể cả chính người gửi: họ có thể đang mở thêm tab khác hoặc điện thoại.
        await _chatNotifier.SendChatMessageAsync(memberIdList, dto);

        return Result.Success(dto);
    }

    public async Task<Result> MarkRead(int id)
    {
        var me = await _db.ConversationMember.FirstOrDefaultAsync(x => x.ConversationId == id && x.UserId == _currentUser.UserId);
        if (me is null) return Result.Forbidden();

        var lastId = await _db.ChatMessage.Where(x => x.ConversationId == id)
            .OrderByDescending(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();

        if (lastId is not null && lastId > (me.LastReadMessageId ?? 0))
        {
            me.LastReadMessageId = lastId;
            await _db.SaveChangesAsync();
        }
        return Result.Success(new MessageResponse("Đã đánh dấu đã đọc."));
    }

    public async Task<Result> DeleteMessage(int id)
    {
        var message = await _db.ChatMessage.FirstOrDefaultAsync(x => x.Id == id);
        if (message is null) return Result.NotFound(new MessageResponse("Không tìm thấy tin nhắn."));
        if (message.SenderUserId != _currentUser.UserId && !_currentUser.IsAdmin) return Result.Forbidden();

        message.IsDeleted = true;

        // Xóa luôn tệp trên đĩa. Thu hồi mà tệp vẫn nằm nguyên chỗ cũ thì người nhận nào đã kịp mở ảnh ra tab
        // mới vẫn xem được mãi mãi — "thu hồi" như vậy chỉ là lời hứa suông.
        if (!string.IsNullOrEmpty(message.AttachmentUrl)) _fileService.Delete(message.AttachmentUrl);

        await _db.SaveChangesAsync();

        var memberIdList = await _db.ConversationMember.AsNoTracking()
            .Where(x => x.ConversationId == message.ConversationId).Select(x => x.UserId).ToListAsync();
        await _chatNotifier.NotifyMessageDeletedAsync(memberIdList, message.ConversationId, message.Id);

        return Result.Success(new MessageResponse("Đã thu hồi tin nhắn."));
    }

    public async Task<Result> UploadAttachment(FileUpload file)
    {
        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Tệp rỗng."));
        if (file.Length > MaxAttachmentByte)
            return Result.Invalid(new MessageResponse($"Tệp vượt quá giới hạn {MaxAttachmentByte / 1024 / 1024} MB."));

        try
        {
            var result = await _fileService.SaveAsync(file, AttachmentFolder);
            return Result.Success(result);
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(new MessageResponse(ex.Message));
        }
    }

    public async Task<Result> GetContactList(string? keyword)
    {
        var userId = _currentUser.UserId;
        var query = _db.User.AsNoTracking().Where(x => x.IsActive && x.Id != userId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(x => x.FullName.Contains(k) || x.EmployeeCode.Contains(k) || x.Email.Contains(k));
        }

        var list = await query
            .OrderBy(x => x.FullName)
            .Take(30)
            .Select(x => new ChatContactDto(x.Id, x.FullName, x.AvatarUrl,
                x.Department != null ? x.Department.Name : null, x.EmployeeCode))
            .ToListAsync();

        return Result.Success(list);
    }

    // ==================== HÀM DÙNG CHUNG ====================

    private Task<bool> IsMemberAsync(int conversationId)
    {
        var userId = _currentUser.UserId;
        return _db.ConversationMember.AsNoTracking()
            .AnyAsync(x => x.ConversationId == conversationId && x.UserId == userId);
    }

    /// <summary>Báo cho mọi thành viên hiện tại rằng hội thoại vừa đổi (lập nhóm, đổi tên, thêm/bớt người) để
    /// hộp chat của họ tải lại danh sách — không có cái này thì nhóm vừa lập chỉ hiện ra ở máy người lập, còn
    /// những người được mời phải tự tải lại trang mới thấy.</summary>
    private async Task NotifyConversationChangedAsync(int conversationId)
    {
        var memberIdList = await _db.ConversationMember.AsNoTracking()
            .Where(x => x.ConversationId == conversationId).Select(x => x.UserId).ToListAsync();

        await _chatNotifier.NotifyConversationChangedAsync(memberIdList, conversationId);
    }

    /// <summary>Đọc tên kiểu tin từ request. Tên lạ (bản giao diện cũ, hoặc ai đó gõ tay) thì coi là tin chữ —
    /// hiển thị ra vẫn đúng nội dung, hơn hẳn việc trả 400 cho một tin người ta đã gõ xong.</summary>
    private static ChatMessageType ParseMessageType(string? value) =>
        Enum.TryParse<ChatMessageType>(value, ignoreCase: true, out var parsed) ? parsed : ChatMessageType.Text;

    /// <summary>Dòng xem trước trong danh sách hội thoại. Tin đính kèm không có chữ nào, để nguyên Content thì
    /// cả danh sách toàn dòng trống — phải mô tả bằng lời xem đó là thứ gì.</summary>
    private static string PreviewOf(ChatMessageType type, string content, string? attachmentName) => type switch
    {
        ChatMessageType.Image => "[Hình ảnh]",
        ChatMessageType.Audio => "[Tin nhắn thoại]",
        ChatMessageType.File => $"[Tệp] {attachmentName}",
        _ => content
    };

    /// <summary>Luôn xếp id nhỏ trước để (7,42) và (42,7) ra cùng một khóa.</summary>
    private static string DirectKeyOf(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";
}
