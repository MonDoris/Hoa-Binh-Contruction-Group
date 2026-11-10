using System.Runtime.CompilerServices;
using HbELearning.Application.Common.Interfaces;
using HbELearning.Application.DTOs;
using HbELearning.Application.Features.AiTutor;
using HbELearning.Application.Services;
using HbELearning.Domain.Constants;
using HbELearning.Domain.Entities;
using HbELearning.Infrastructure.Persistence;
using HbELearning.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HbELearning.Tests.Ai;

/// <summary>HB Minerva ghi nhớ câu trả lời: hỏi lại cùng nội dung thì trả ngay, KHÔNG gọi model — nhưng chỉ khi các đoạn
/// tài liệu làm căn cứ vẫn còn và người hỏi có quyền đọc chúng.</summary>
public class AiTutorAnswerMemoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakeChat _chat = new();
    private readonly FakeDocumentProcessing _documentProcessing = new();
    private readonly int _adminId;
    private readonly int _learnerId;

    /// <summary>Đoạn của một khóa học (chỉ ai được đọc khóa 10 mới tìm ra) và đoạn dùng chung toàn công ty.</summary>
    private static readonly DocumentChunkMatchDto CourseChunk = new(1, "noi-quy.pdf", 0, "Giờ vào làm 8h00.", 0.9, 10, null);
    private static readonly DocumentChunkMatchDto CompanyChunk = new(2, "so-tay.pdf", 0, "Giờ tan làm 17h00.", 0.8, null, null);

    public AiTutorAnswerMemoryTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        _adminId = AddUser(db, "AD01", RoleName.Admin);
        _learnerId = AddUser(db, "HV01", RoleName.Learner);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Same_question_reworded_is_answered_from_memory_without_the_model()
    {
        _documentProcessing.ChunkList = [CompanyChunk];

        var first = await AskAsync(_learnerId, "Mấy giờ tan làm?");
        Assert.Equal(1, _chat.CallCount);

        var second = await AskAsync(_learnerId, "cho em hỏi giờ tan làm là mấy giờ ạ");
        Assert.Equal(1, _chat.CallCount);
        Assert.Equal(first, second);

        using var db = new AppDbContext(_options);
        var memory = Assert.Single(db.AiAnswerMemory);
        Assert.Equal(1, memory.HitCount);
        Assert.Equal("Mấy giờ tan làm?", memory.Question);
    }

    [Fact]
    public async Task Different_question_on_the_same_document_goes_to_the_model()
    {
        _documentProcessing.ChunkList = [CompanyChunk];

        await AskAsync(_learnerId, "Mấy giờ tan làm?");
        await AskAsync(_learnerId, "Mấy giờ vào làm?");

        Assert.Equal(2, _chat.CallCount);
    }

    [Fact]
    public async Task Answer_based_on_a_course_the_asker_cannot_read_is_not_reused()
    {
        _documentProcessing.ChunkList = [CourseChunk, CompanyChunk];

        // Admin đọc được mọi khóa: câu trả lời dựa trên cả đoạn của khóa 10.
        await AskAsync(_adminId, "Mấy giờ vào làm?");
        // Học viên không được phân công khóa 10 (và khóa đó không xuất bản) thì không được nhận lại câu trả lời ấy.
        await AskAsync(_learnerId, "Mấy giờ vào làm?");

        Assert.Equal(2, _chat.CallCount);
    }

    [Fact]
    public async Task Reloaded_document_invalidates_the_memory()
    {
        _documentProcessing.ChunkList = [CompanyChunk];
        await AskAsync(_learnerId, "Mấy giờ tan làm?");

        // Nạp lại tài liệu = đoạn mới, mã mới.
        _documentProcessing.ChunkList = [CompanyChunk with { Id = 3, TextContent = "Giờ tan làm 17h30." }];
        await AskAsync(_learnerId, "Mấy giờ tan làm?");

        Assert.Equal(2, _chat.CallCount);
    }

    [Fact]
    public async Task No_information_answer_is_not_remembered()
    {
        _documentProcessing.ChunkList = [CompanyChunk];
        _chat.Answer = IAiChatService.NoInformationAnswer;

        await AskAsync(_learnerId, "Công ty có phòng gym không?");
        await AskAsync(_learnerId, "Công ty có phòng gym không?");

        Assert.Equal(2, _chat.CallCount);
        using var db = new AppDbContext(_options);
        Assert.Empty(db.AiAnswerMemory);
    }

    [Fact]
    public async Task Reworded_no_information_answer_is_not_remembered()
    {
        _documentProcessing.ChunkList = [CompanyChunk];
        _chat.Answer = "Tài liệu hiện tại chưa có thông tin về thời gian tan làm.";

        await AskAsync(_learnerId, "Công ty có phòng gym không?");

        using var db = new AppDbContext(_options);
        Assert.Empty(db.AiAnswerMemory);
    }

    [Fact]
    public async Task Changed_prompt_version_stops_old_answers_from_being_reused()
    {
        _documentProcessing.ChunkList = [CompanyChunk];
        await AskAsync(_learnerId, "Mấy giờ tan làm?");

        _chat.PromptVersion = "v2";
        await AskAsync(_learnerId, "Mấy giờ tan làm?");

        Assert.Equal(2, _chat.CallCount);
    }

    [Fact]
    public async Task Follow_up_question_mid_conversation_is_not_remembered()
    {
        _documentProcessing.ChunkList = [CompanyChunk];

        var events = new RecordingEventStream();
        await NewService(_learnerId).Ask(new AiTutorAskRequest("Giờ làm việc của công ty thế nào?", null, []), events, default);
        Assert.NotNull(events.SessionId);

        // Câu thứ hai trong cùng hội thoại: model trả lời có dựa vào lượt trước, không đem cho người khác.
        await NewService(_learnerId).Ask(new AiTutorAskRequest("Thứ bảy có phải đi làm không?", events.SessionId, []), new RecordingEventStream(), default);

        using var db = new AppDbContext(_options);
        Assert.Single(db.AiAnswerMemory);
    }

    private async Task<string> AskAsync(int userId, string question)
    {
        var events = new RecordingEventStream();
        await NewService(userId).Ask(new AiTutorAskRequest(question, null, []), events, default);
        Assert.Null(events.Error);
        return events.Answer;
    }

    private AiTutorService NewService(int userId)
    {
        var db = new AppDbContext(_options);
        var currentUser = new TestCurrentUser { UserId = userId, IsAdmin = userId == _adminId };
        return new AiTutorService(db, _documentProcessing, new NullEmbedding(), _chat, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AiTutorService>.Instance, currentUser, new NullTextExtractor(), new NullFileService());
    }

    private static int AddUser(AppDbContext db, string code, string roleName)
    {
        var user = new User
        {
            EmployeeCode = code, FullName = code, Email = $"{code.ToLowerInvariant()}@test.local", PasswordHash = "x",
            RoleId = db.Role.Single(r => r.Name == roleName).Id,
        };
        db.User.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    /// <summary>Kho tri thức giả: trả các đoạn bài test đặt sẵn, qua đúng hàng rào quyền đọc theo khóa học.</summary>
    private sealed class FakeDocumentProcessing : IDocumentProcessingService
    {
        public List<DocumentChunkMatchDto> ChunkList { get; set; } = [];

        public Task<List<DocumentChunkMatchDto>> GetAnswerContextAsync(string question, int topCount = 5, int? courseId = null,
            List<int>? allowedCourseIdList = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ChunkList
                .Where(x => allowedCourseIdList is null || x.CourseId is null || allowedCourseIdList.Contains(x.CourseId.Value))
                .ToList());

        public Task PrefetchQueryVectorAsync(string query, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<DocumentChunkMatchDto>> SearchAsync(string query, int topCount = 5, int? courseId = null,
            List<int>? allowedCourseIdList = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentIngestResultDto> ProcessAndSaveDocumentAsync(string filePath, string? fileType = null, int? courseId = null,
            int? lessonId = null, string? sourceFileUrl = null, string? displayFileName = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> DeleteBySourceAsync(string sourceFileName, int? courseId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Model giả: đếm số lần được gọi — gọi tới model là KHÔNG dùng được trí nhớ.</summary>
    private sealed class FakeChat : IAiChatService
    {
        public int CallCount { get; private set; }
        public string Answer { get; set; } = "Câu trả lời của model.";

        public string Model => "fake-model";
        public string PromptVersion { get; set; } = "v1";
        public string VisionModel => "fake-vision";

        public async IAsyncEnumerable<string> StreamAnswerAsync(string question, IReadOnlyList<DocumentChunkMatchDto> contextList,
            IReadOnlyList<AiChatTurnDto>? historyList = null, bool isCitationEnabled = true,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Yield();
            yield return $"{Answer} ({question})";
        }

        public IAsyncEnumerable<string> StreamAttachmentAnswerAsync(string question, string attachmentText, IReadOnlyList<byte[]> imageList,
            IReadOnlyList<AiChatTurnDto>? historyList = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public List<DocumentChunkMatchDto> TrimToPromptBudget(IReadOnlyList<DocumentChunkMatchDto> contextList) => [.. contextList];
    }

    private sealed class RecordingEventStream : IEventStream
    {
        private readonly System.Text.StringBuilder _answer = new();
        public string Answer => _answer.ToString();
        public int? SessionId { get; private set; }
        public string? Error { get; private set; }

        public Task SendAsync(string eventName, object data, CancellationToken cancellationToken)
        {
            switch (eventName)
            {
                case "token": _answer.Append((string)data.GetType().GetProperty("t")!.GetValue(data)!); break;
                case "session": SessionId = ((AiChatSessionDto)data).Id; break;
                case "error": Error = ((MessageResponse)data).Message; break;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class NullEmbedding : IEmbeddingService
    {
        public string Model => "fake-embed";
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<float[]>> GetEmbeddingListAsync(IReadOnlyList<string> textList, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiDocumentStatusDto> CheckStatusAsync(string? chatModel = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NullTextExtractor : IDocumentTextExtractor
    {
        public Task<string> ExtractTextAsync(string filePath, string extension, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
