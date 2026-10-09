// File: Application/Features/AiTutor/AiTutorService.cs
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.AiTutor;

/// <summary>Pico B của học viên: một khung chat hỏi đáp dựa trên kho tri thức đã có sẵn.
///
/// Đây là bản DÀNH CHO NGƯỜI HỌC của <see cref="AiDocumentService"/>, tách riêng chứ không nới quyền
/// controller kia, vì hai màn hình khác nhau ở đúng ba điểm không thể trộn chung:
///   1. Không có nạp/xóa tài liệu — học viên chỉ hỏi, việc nuôi kho tri thức vẫn là của Admin.
///   2. Không lộ hạ tầng — không trả về địa chỉ máy chủ AI, tên model hay danh sách tài liệu đã nạp.
///   3. Có hàng rào dữ liệu — chỉ đọc tài liệu dùng chung toàn công ty, các khóa học đã xuất bản và các
///      khóa học chính người hỏi được phân công (xem <see cref="GetReadableCourseIdListAsync"/>).
///
/// Kho tri thức mà nó tra gồm HAI nguồn, và người hỏi không cần biết mình đang đọc nguồn nào: tài liệu
/// PDF/Word Admin tải lên, và NỘI DUNG CÁC KHÓA HỌC (cả file đính kèm bài học) do máy tự đọc ngay khi xuất
/// bản / sửa bài và quét lại mỗi vài giờ (xem
/// <see cref="Services.ICourseKnowledgeService"/>). Cả hai đều mang mã khóa học nên cùng đi qua đúng một
/// hàng rào quyền đọc ở trên.
///
/// Mỗi lượt hỏi được ghi lại thành lịch sử để người học mở xem lại — xem phần "LỊCH SỬ HỘI THOẠI" bên dưới.
/// Vài lượt gần nhất của cùng hội thoại còn được đưa lại cho model, nhờ đó người học hỏi nối tiếp được thay
/// vì phải nhắc lại chủ đề trong từng câu.</summary>
public class AiTutorService : IAiTutorService
{
    private readonly IAppDbContext _db;
    private readonly IDocumentProcessingService _documentProcessing;
    private readonly IEmbeddingService _embedding;
    private readonly IAiChatService _chat;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiTutorService> _logger;
    private readonly ICurrentUser _currentUser;

    public AiTutorService(IAppDbContext db, IDocumentProcessingService documentProcessing, IEmbeddingService embedding, IAiChatService chat, IMemoryCache cache, ILogger<AiTutorService> logger, ICurrentUser currentUser)
    {
        _db = db;
        _documentProcessing = documentProcessing;
        _embedding = embedding;
        _chat = chat;
        _cache = cache;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<Result> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _embedding.CheckStatusAsync(_chat.Model, cancellationToken);

        // Khác màn hình quản trị: ở đây thiếu model sinh câu trả lời là HỎNG HẲN, vì trang này không có
        // việc gì khác ngoài hỏi đáp. Bên quản trị vẫn coi là "sẵn sàng" vì còn nạp tài liệu được.
        var isReady = status.IsReady && status.IsChatReady;

        return Result.Success(new AiTutorStatusDto(
            isReady,
            isReady
                ? "Pico B đã sẵn sàng."
                : "Pico B đang tạm nghỉ. Vui lòng liên hệ quản trị hệ thống."));
    }

    public async Task Ask(AiTutorAskRequest request, IEventStream events, CancellationToken cancellationToken)
    {
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            await events.SendAsync("error", new MessageResponse("Vui lòng nhập câu hỏi."), cancellationToken);
            return;
        }

        if (question.Length > MaxQuestionLength)
        {
            await events.SendAsync("error", new MessageResponse(
                $"Câu hỏi dài quá {MaxQuestionLength} ký tự. Hãy tách thành vài câu hỏi ngắn."), cancellationToken);
            return;
        }

        // Mỗi người chỉ được một câu trả lời đang chạy. Không chặn thì một người bấm gửi liên tục là chiếm
        // hết CPU của máy chủ AI, và cả công ty cùng ngồi chờ — trang này mở cho MỌI nhân viên, khác hẳn
        // màn hình quản trị vốn chỉ vài tài khoản dùng.
        if (!AnsweringUserIdList.TryAdd(_currentUser.UserId, 0))
        {
            await events.SendAsync("error", new MessageResponse(
                "Bạn đang có một câu hỏi chưa trả lời xong. Hãy đợi câu đó xong rồi hỏi tiếp."), cancellationToken);
            return;
        }

        try
        {
            // Hội thoại đang mở, hoặc một hội thoại mới nếu đây là câu đầu tiên. Tra theo CẢ UserId chứ không
            // chỉ theo Id: thiếu điều kiện đó thì chỉ cần sửa một con số trong yêu cầu là ghi được câu hỏi vào
            // hội thoại của người khác, và đọc luôn được vài lượt hỏi đáp cũ của họ qua phần ngữ cảnh bên dưới.
            var session = await GetOrCreateSessionAsync(request.SessionId, question, cancellationToken);
            var historyList = await GetRecentTurnListAsync(session.Id, cancellationToken);

            // Báo mã hội thoại NGAY, trước khi model chạy: người học bấm Dừng hoặc đóng trang giữa chừng thì
            // trình duyệt vẫn biết câu hỏi vừa rồi nằm ở hội thoại nào để mở lại.
            await events.SendAsync("session", new AiChatSessionDto(
                session.Id, session.Title, 0, session.CreatedAt, session.UpdatedAt), cancellationToken);

            _db.AiChatMessage.Add(new AiChatMessage
            {
                AiChatSessionId = session.Id,
                Role = AiChatRole.User,
                Content = question
            });
            session.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync(cancellationToken);

            // Câu trả lời CÀI SẴN được xét TRƯỚC khi tra tài liệu. Những câu xã giao ("hi", "cảm ơn") không
            // nằm trong tài liệu nội bộ nào, mà chúng lại quá ngắn nên vector của chúng gần như nhau với mọi
            // đoạn — thả vào đường tìm kiếm ngữ nghĩa là chắc chắn vớ được một đoạn bất kỳ rồi model viết ra
            // một bài giảng lạc đề, mất cả chục giây CPU để cho ra một câu trả lời sai chủ đề.
            var quickAnswer = await FindQuickReplyAsync(question, cancellationToken);
            if (quickAnswer is not null)
            {
                await events.SendAsync("token", new { t = quickAnswer }, cancellationToken);
                await SaveAnswerAsync(session, quickAnswer, []);
                await events.SendAsync("done", new { }, cancellationToken);
                return;
            }

            var readableCourseIdList = await GetReadableCourseIdListAsync(cancellationToken);

            var foundList = await _documentProcessing.GetAnswerContextAsync(
                BuildSearchQuery(question, historyList), topCount: 4, courseId: null,
                allowedCourseIdList: readableCourseIdList, cancellationToken: cancellationToken);

            // Cắt theo đúng ngân sách câu lệnh TRƯỚC khi lập bảng nguồn: danh sách sau khi cắt vừa là thứ
            // gửi cho model, vừa là bảng "AI đã dựa vào các đoạn sau" hiện cho người hỏi đối chiếu.
            var contextList = _chat.TrimToPromptBudget(foundList);

            // Danh sách nguồn vẫn được dựng để GHI LẠI trong lịch sử — quản trị viên cần biết một câu trả lời
            // đã dựa trên đoạn tài liệu nào khi có người thắc mắc. Nhưng nó KHÔNG được gửi xuống trình duyệt
            // của học viên nữa: trang bên đó đã bỏ hẳn bảng nguồn (người học không biết mấy tên file ấy là gì),
            // nên gửi kèm chỉ là đẩy vài nghìn ký tự tài liệu qua mạng ở mỗi câu hỏi để rồi vứt đi.
            var sourceList = contextList
                .Select((x, i) => new AiAnswerSourceDto(i + 1, x.SourceFileName, x.ChunkIndex, x.TextContent, x.Score))
                .ToList();

            if (contextList.Count == 0)
            {
                await events.SendAsync("token", new { t = NoContextAnswer }, cancellationToken);
                await SaveAnswerAsync(session, NoContextAnswer, sourceList);
                await events.SendAsync("done", new { }, cancellationToken);
                return;
            }

            // Cả phòng cùng học một khóa thì cùng vướng đúng mấy chỗ như nhau, mà mỗi lượt hỏi tốn của model
            // hàng chục giây cho một câu trả lời y hệt lần trước. Khóa đệm gồm mã CÁC ĐOẠN tài liệu đã đưa
            // cho model, nên nó cũng chính là hàng rào quyền: hai người có phạm vi đọc khác nhau sẽ tìm ra
            // bộ đoạn khác nhau, tức khóa khác nhau, không ai nhận nhầm bản đệm của ai.
            //
            // Câu hỏi NỐI TIẾP thì không dùng bộ nhớ đệm: cùng một câu "thế còn trường hợp kia?" mang nghĩa
            // khác hẳn nhau tùy hội thoại, mà khóa đệm chỉ gồm câu hỏi và các đoạn tài liệu nên không phân
            // biệt nổi — dùng bừa là người này nhận đúng câu trả lời cho ngữ cảnh của người khác.
            var cacheKey = historyList.Count == 0 ? BuildAnswerCacheKey(question, contextList) : null;
            if (cacheKey is not null
                && _cache.TryGetValue<string>(cacheKey, out var cachedAnswer)
                && cachedAnswer is { Length: > 0 })
            {
                await events.SendAsync("token", new { t = cachedAnswer }, cancellationToken);
                await SaveAnswerAsync(session, cachedAnswer, sourceList);
                await events.SendAsync("done", new { }, cancellationToken);
                return;
            }

            var answerBuilder = new StringBuilder();
            // isCitationEnabled: false — học viên không xem bảng nguồn, nên để model chèn [1] [2] vào chỉ tạo
            // ra những con số trỏ vào hư không.
            await foreach (var piece in _chat.StreamAnswerAsync(
                question, contextList, historyList, isCitationEnabled: false, cancellationToken))
            {
                answerBuilder.Append(piece);
                await events.SendAsync("token", new { t = piece }, cancellationToken);
            }

            // Chỉ đưa vào bộ nhớ đệm khi model đã viết XONG. Người hỏi bỏ ngang giữa chừng thì phần chữ dở
            // dang không phải câu trả lời, mà lưu vào rồi thì mọi người hỏi lại sau đó đều nhận đúng mẩu cụt ấy.
            if (cacheKey is not null && answerBuilder.Length > 0 && !cancellationToken.IsCancellationRequested)
                _cache.Set(cacheKey, answerBuilder.ToString(), AnswerCacheDuration);

            // LỊCH SỬ thì ngược lại: phần chữ dở dang vẫn được ghi. Người học bấm Dừng là vì đã đọc đủ ý mình
            // cần, và thứ họ mở lại ngày mai phải đúng bằng thứ họ đã nhìn thấy trên màn hình.
            await SaveAnswerAsync(session, answerBuilder.ToString(), sourceList);

            await events.SendAsync("done", new { }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Học viên rời trang / bấm Dừng giữa chừng — không phải lỗi, không cần ghi log.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gia su AI tra loi that bai cho nguoi dung {UserId}.", _currentUser.UserId);
            await events.SendAsync("error", new MessageResponse(
                "Pico B chưa trả lời được. Vui lòng thử lại hoặc liên hệ quản trị hệ thống."), CancellationToken.None);
        }
        finally
        {
            AnsweringUserIdList.TryRemove(_currentUser.UserId, out _);
        }
    }

    public async Task<Result> GetSessionList(CancellationToken cancellationToken)
        => Result.Success(await _db.AiChatSession.AsNoTracking()
            .Where(x => x.UserId == _currentUser.UserId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(MaxSessionListCount)
            .Select(x => new AiChatSessionDto(
                x.Id, x.Title, x.MessageList.Count, x.CreatedAt, x.UpdatedAt))
            .ToListAsync(cancellationToken));

    public async Task<Result> GetSession(int id, CancellationToken cancellationToken)
    {
        var session = await _db.AiChatSession.AsNoTracking()
            .Where(x => x.Id == id && x.UserId == _currentUser.UserId)
            .Select(x => new { x.Id, x.Title, x.CreatedAt, x.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        // Hội thoại của người khác cũng trả về "không tìm thấy", không phải 403: câu trả lời "có tồn tại
        // nhưng không phải của bạn" tự nó đã là một mẩu thông tin về người khác.
        if (session is null) return Result.NotFound(new MessageResponse("Không tìm thấy hội thoại này."));

        var messageList = await _db.AiChatMessage.AsNoTracking()
            .Where(x => x.AiChatSessionId == id)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Role, x.Content, x.SourceJson, x.CreatedAt })
            .ToListAsync(cancellationToken);

        return Result.Success(new AiChatSessionDetailDto(
            session.Id, session.Title, session.CreatedAt, session.UpdatedAt,
            [.. messageList.Select(x => new AiChatMessageDto(
                x.Id,
                x.Role == AiChatRole.User ? "user" : "assistant",
                x.Content,
                // Luôn rỗng: bảng nguồn đã bỏ khỏi trang của học viên, mà lịch sử thì chính họ mở lại.
                // Dữ liệu gốc vẫn nằm nguyên trong cột SourceJson cho quản trị viên tra khi cần.
                [],
                x.CreatedAt))]));
    }

    public async Task<Result> RenameSession(int id, AiChatSessionRenameRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return Result.Invalid(new MessageResponse("Tên hội thoại không được để trống."));

        var session = await _db.AiChatSession
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == _currentUser.UserId, cancellationToken);
        if (session is null) return Result.NotFound(new MessageResponse("Không tìm thấy hội thoại này."));

        session.Title = TrimTitle(title);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new MessageResponse("Đã đổi tên hội thoại."));
    }

    public async Task<Result> DeleteSession(int id, CancellationToken cancellationToken)
    {
        var removedCount = await _db.AiChatSession
            .Where(x => x.Id == id && x.UserId == _currentUser.UserId)
            .ExecuteDeleteAsync(cancellationToken);

        return removedCount == 0
            ? Result.NotFound(new MessageResponse("Không tìm thấy hội thoại này."))
            : Result.Success(new MessageResponse("Đã xóa hội thoại."));
    }

    public async Task<Result> DeleteAllSession(CancellationToken cancellationToken)
    {
        var removedCount = await _db.AiChatSession
            .Where(x => x.UserId == _currentUser.UserId)
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success(new MessageResponse($"Đã xóa {removedCount} hội thoại."));
    }

    /// <summary>Hội thoại đang mở, hoặc một hội thoại mới đặt tên theo câu hỏi đầu tiên.
    ///
    /// Mã hội thoại người gửi lên mà không tra ra (đã xóa ở tab khác, hoặc là của người khác) thì MỞ HỘI
    /// THOẠI MỚI chứ không báo lỗi: người dùng vừa gõ xong một câu hỏi, việc của họ là nhận câu trả lời chứ
    /// không phải nghe giải thích về một mã hội thoại mà giao diện tự giữ.</summary>
    private async Task<AiChatSession> GetOrCreateSessionAsync(
        int? sessionId, string firstQuestion, CancellationToken cancellationToken)
    {
        if (sessionId is not null)
        {
            var existing = await _db.AiChatSession
                .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == _currentUser.UserId, cancellationToken);
            if (existing is not null) return existing;
        }

        var session = new AiChatSession
        {
            UserId = _currentUser.UserId,
            Title = TrimTitle(firstQuestion)
        };

        _db.AiChatSession.Add(session);
        await _db.SaveChangesAsync(cancellationToken);
        return session;
    }

    /// <summary>Vài lượt hỏi-đáp gần nhất của hội thoại, cũ trước mới sau — phần ngữ cảnh giúp model hiểu
    /// được câu hỏi nối tiếp. Chỉ ghép những lượt ĐÃ CÓ CÂU TRẢ LỜI: một câu hỏi bị bỏ dở (người dùng đóng
    /// trang) không nói lên điều gì về chủ đề đang bàn.</summary>
    private async Task<List<AiChatTurnDto>> GetRecentTurnListAsync(int sessionId, CancellationToken cancellationToken)
    {
        var messageList = await _db.AiChatMessage.AsNoTracking()
            .Where(x => x.AiChatSessionId == sessionId)
            .OrderByDescending(x => x.Id)
            .Take(MaxHistoryTurn * 2)
            .Select(x => new { x.Role, x.Content })
            .ToListAsync(cancellationToken);

        messageList.Reverse();

        var turnList = new List<AiChatTurnDto>();
        for (int i = 0; i + 1 < messageList.Count; i++)
        {
            if (messageList[i].Role != AiChatRole.User || messageList[i + 1].Role != AiChatRole.Assistant) continue;

            turnList.Add(new AiChatTurnDto(messageList[i].Content, messageList[i + 1].Content));
            i++;
        }

        return turnList;
    }

    /// <summary>Ghi câu trả lời vào lịch sử. Dùng CancellationToken.None có chủ đích: hàm này hay được gọi
    /// đúng lúc người dùng vừa bấm Dừng, mà đó chính là lúc cần ghi lại nhất — truyền token đang bị hủy vào
    /// thì lệnh ghi chết ngay và lượt hỏi đó biến mất khỏi lịch sử.</summary>
    private async Task SaveAnswerAsync(AiChatSession session, string answer, List<AiAnswerSourceDto> sourceList)
    {
        if (string.IsNullOrWhiteSpace(answer)) return;

        _db.AiChatMessage.Add(new AiChatMessage
        {
            AiChatSessionId = session.Id,
            Role = AiChatRole.Assistant,
            Content = answer,
            SourceJson = sourceList.Count > 0 ? JsonSerializer.Serialize(sourceList, SseJsonOption) : null
        });

        session.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Câu trả lời cài sẵn khớp câu hỏi này (đã thay tên người hỏi vào), hoặc null nếu không có.
    ///
    /// Danh sách được giữ trong bộ nhớ đệm: bảng này bé và gần như không đổi, mà nó nằm trên đường đi của
    /// MỌI câu hỏi của MỌI người — đọc database ở mỗi lượt chỉ để thường xuyên nhận về đúng vài dòng cũ.
    /// Màn hình quản trị xóa đệm ngay khi có người sửa (xem <see cref="AiQuickReplyService.CacheKey"/>),
    /// nên sửa xong là có hiệu lực liền chứ không phải chờ hết hạn.</summary>
    private async Task<string?> FindQuickReplyAsync(string question, CancellationToken cancellationToken)
    {
        var replyList = await _cache.GetOrCreateAsync(AiQuickReplyService.CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = QuickReplyCacheDuration;
            return await _db.AiQuickReply.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
        });

        if (replyList is null || replyList.Count == 0) return null;

        var matched = QuickReplyMatcher.Match(question, replyList);
        if (matched is null) return null;

        var profile = await _db.User.AsNoTracking()
            .Where(x => x.Id == _currentUser.UserId)
            .Select(x => new { x.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        return QuickReplyMatcher.ApplyPlaceholder(matched.Answer, profile?.FullName, _currentUser.Role);
    }

    /// <summary>Bộ nhớ đệm danh sách câu cài sẵn sống bao lâu nếu không ai sửa gì. Đặt ngắn vì nó chỉ là lưới
    /// an toàn: đường sửa bình thường đã tự xóa đệm, con số này chỉ để một bản sửa thẳng trong database cũng
    /// có hiệu lực sau vài phút thay vì nằm lại tới lần khởi động sau.</summary>
    private static readonly TimeSpan QuickReplyCacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>Câu để đi tìm tài liệu, không phải câu gửi cho model.
    ///
    /// Câu hỏi nối tiếp thường rất ngắn và không có lấy một từ khóa nào ("còn nửa ngày thì sao?"): đem đúng
    /// chừng ấy chữ đi tìm ngữ nghĩa thì ra một nắm đoạn ngẫu nhiên, và model dù có nhớ ngữ cảnh cũng không
    /// còn tài liệu đúng để mà đọc. Ghép thêm câu hỏi liền trước là đủ để kéo phép tìm về đúng chủ đề.
    ///
    /// Chỉ ghép với câu NGẮN: câu hỏi dài đã tự đủ từ khóa, ghép thêm chỉ làm loãng vector và kéo kết quả
    /// về chủ đề cũ trong khi người dùng vừa chuyển sang chuyện khác.</summary>
    private static string BuildSearchQuery(string question, IReadOnlyList<AiChatTurnDto> historyList)
    {
        if (historyList.Count == 0 || question.Length > ShortQuestionLength) return question;

        return $"{historyList[^1].Question} {question}";
    }

    /// <summary>Câu hỏi đầu tiên -> tên hội thoại: gom khoảng trắng và cắt vừa cột Title (150 ký tự).</summary>
    private static string TrimTitle(string text)
    {
        var title = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)] + "…";
    }

    /// <summary>Số hội thoại cũ trả về cho danh sách lịch sử. Có trần để một người hỏi cả năm không biến
    /// thanh lịch sử thành một danh sách vài nghìn dòng phải tải hết mỗi lần mở trang.</summary>
    private const int MaxSessionListCount = 100;

    /// <summary>Số lượt hỏi-đáp cũ lấy ra làm ngữ cảnh. Khớp với trần bên OllamaChatService — lấy nhiều hơn
    /// mức model dùng chỉ tốn thêm một lượt đọc database rồi bị cắt bỏ.</summary>
    private const int MaxHistoryTurn = 2;

    /// <summary>Câu hỏi ngắn hơn ngần này ký tự thì coi là câu hỏi nối tiếp và được ghép thêm ngữ cảnh khi
    /// đi tìm tài liệu (xem <see cref="BuildSearchQuery"/>).</summary>
    private const int ShortQuestionLength = 60;

    /// <summary>Trần độ dài tên hội thoại — khớp MaxLength của cột Title.</summary>
    private const int MaxTitleLength = 150;

    /// <summary>
    /// Những khóa học người đang hỏi được phép đọc tài liệu. Trả null nghĩa là KHÔNG rào — dành cho các vai
    /// trò vốn đã xem được mọi khóa học ở màn hình Quản lý khóa học (xem <see cref="RoleName.CourseViewers"/>);
    /// rào họ lại ở đây chỉ tạo ra một chỗ duy nhất trong hệ thống giấu bớt thứ họ mở trang bên cạnh là thấy.
    ///
    /// Còn lại (học viên, và các vai trò chỉ học chứ không quản nội dung) đọc được:
    ///   - MỌI khóa học ĐÃ XUẤT BẢN — ai cũng mở được những khóa này ở danh mục khóa học (xem CourseService),
    ///     nên Pico B giấu chúng đi chỉ khiến người học hỏi đúng nội dung bài mà nhận về "chưa có thông tin";
    ///   - khóa học mình đã được phân công dù hiện không còn ở trạng thái xuất bản (đã lưu trữ nhưng còn học dở).
    /// Bản nháp / đang chờ duyệt thì không bao giờ lọt qua — cũng vì vậy mà chúng không được nạp ngay từ đầu.
    /// Tài liệu dùng chung toàn công ty không nằm trong danh sách này vì nó không thuộc khóa nào — phần lọc ở
    /// DocumentProcessingService luôn cho những đoạn đó qua.
    /// </summary>
    private async Task<List<int>?> GetReadableCourseIdListAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.IsAdmin || _currentUser.IsTrainer || _currentUser.IsInstructor || _currentUser.IsHeadOfDepartment || _currentUser.IsDirector) return null;

        var userId = _currentUser.UserId;
        return await _db.Course.AsNoTracking()
            .Where(x => x.Status == CourseStatus.Published || x.EnrollmentList.Any(e => e.UserId == userId))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Câu trả lời khi không tìm được đoạn tài liệu nào liên quan. Viết đúng giọng của câu lệnh hệ
    /// thống trong OllamaChatService để học viên không thấy hai kiểu từ chối khác nhau cho cùng một tình huống.</summary>
    private const string NoContextAnswer =
        "Tài liệu nội bộ hiện chưa có thông tin về nội dung này. " +
        "Bạn thử hỏi lại bằng từ ngữ trong bài học, hoặc hỏi trực tiếp người phụ trách đào tạo nhé.";

    /// <summary>Trần độ dài câu hỏi. Khớp maxlength của ô nhập bên trang chat — đặt lại ở đây vì trình duyệt
    /// là thứ sửa được, mà mỗi ký tự thừa đều là thời gian model phải đọc.</summary>
    private const int MaxQuestionLength = 500;

    /// <summary>Câu trả lời nằm trong bộ nhớ đệm bao lâu. Đặt theo ngày chứ không theo giờ vì kho tài liệu
    /// đổi rất thưa, mà khóa đệm đã gồm mã các đoạn tài liệu: nạp lại tài liệu là sinh đoạn mới với mã mới,
    /// nên bản đệm cũ tự hết hiệu lực chứ không cần chờ hết hạn.</summary>
    private static readonly TimeSpan AnswerCacheDuration = TimeSpan.FromHours(24);

    /// <summary>Những người đang có một câu trả lời chạy dở. Dùng ConcurrentDictionary làm tập hợp vì .NET
    /// không có ConcurrentHashSet; giá trị không mang ý nghĩa gì.</summary>
    private static readonly ConcurrentDictionary<int, byte> AnsweringUserIdList = new();

    /// <summary>Khóa đệm gồm: câu hỏi (bỏ qua khác biệt hoa thường và khoảng trắng thừa), model trả lời, và
    /// mã CÁC ĐOẠN TÀI LIỆU đã đưa cho model. Cùng tiền tố "ai-answer|" với màn hình quản trị là cố ý: cùng
    /// một câu hỏi trên cùng bộ đoạn tài liệu thì hai bên dùng chung được một bản trả lời.</summary>
    private string BuildAnswerCacheKey(string question, IReadOnlyList<DocumentChunkMatchDto> contextList)
    {
        var normalizedQuestion = string.Join(' ',
            question.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return $"ai-answer|{_chat.Model}||{string.Join(',', contextList.Select(x => x.Id))}|{normalizedQuestion}";
    }

    private static readonly JsonSerializerOptions SseJsonOption = new(JsonSerializerDefaults.Web);
}
