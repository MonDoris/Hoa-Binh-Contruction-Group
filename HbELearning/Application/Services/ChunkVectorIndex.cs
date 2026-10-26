// File: Application/Services/ChunkVectorIndex.cs
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

/// <summary>Bản chụp kho vector nằm trong bộ nhớ, dùng chung cho cả tiến trình.
///
/// Trước đây MỖI câu hỏi của MỌI người đều kéo toàn bộ cột chữ + cột vector của bảng DocumentChunk lên từ
/// database, giải mã byte thành float rồi tách từ bằng regex cho từng đoạn — cả chục MB và hàng nghìn lần
/// regex trước khi model được gọi. Kho này đổi rất thưa (nạp tài liệu, đồng bộ khóa học) nên làm một lần rồi giữ.
///
/// Làm tươi bằng "vân tay" thay vì móc vào từng chỗ ghi: có ba nơi ghi bảng này (nạp tài liệu, đồng bộ khóa học,
/// xóa bài học) và thêm nơi thứ tư mà quên gọi hàm xóa đệm là AI trả lời bằng nội dung cũ. Vân tay là MỘT truy
/// vấn tổng hợp không đụng tới cột BLOB (đếm dòng, Id lớn nhất, tổng mã khóa học / bài học) nên rẻ hơn tải lại
/// hàng trăm lần; mọi lần thêm, xóa, hay tách bài học khỏi tài liệu đều làm vân tay đổi.</summary>
public sealed class ChunkVectorIndex
{
    public sealed class Entry
    {
        public required int Id { get; init; }
        public required string SourceFileName { get; init; }
        public required int ChunkIndex { get; init; }
        public required string TextContent { get; init; }
        public required int? CourseId { get; init; }
        public required int? LessonId { get; init; }
        public required float[] Vector { get; init; }
        public required float Norm { get; init; }

        /// <summary>Chữ đã chuẩn hóa dạng " a b c " để dò cặp âm tiết bằng một phép Contains.</summary>
        public required string NormalizedText { get; init; }
    }

    private sealed record Snapshot(string Model, (int Count, int MaxId, long CourseSum, long LessonSum) Fingerprint, Entry[] EntryList);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Snapshot? _snapshot;

    public async Task<Entry[]> GetAsync(IAppDbContext db, string model, CancellationToken cancellationToken)
    {
        var fingerprint = await ReadFingerprintAsync(db, model, cancellationToken);

        var current = _snapshot;
        if (current is not null && current.Model == model && current.Fingerprint == fingerprint)
            return current.EntryList;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Người khác có thể đã dựng xong trong lúc ta chờ khóa.
            current = _snapshot;
            if (current is not null && current.Model == model && current.Fingerprint == fingerprint)
                return current.EntryList;

            var rowList = await db.DocumentChunk.AsNoTracking()
                .Where(x => x.EmbeddingModel == model)
                .Select(x => new { x.Id, x.SourceFileName, x.ChunkIndex, x.TextContent, x.VectorData, x.CourseId, x.LessonId })
                .ToListAsync(cancellationToken);

            var entryList = new Entry[rowList.Count];
            for (var i = 0; i < rowList.Count; i++)
            {
                var row = rowList[i];
                var vector = VectorHelper.ToFloatArray(row.VectorData);
                entryList[i] = new Entry
                {
                    Id = row.Id,
                    SourceFileName = row.SourceFileName,
                    ChunkIndex = row.ChunkIndex,
                    TextContent = row.TextContent,
                    CourseId = row.CourseId,
                    LessonId = row.LessonId,
                    Vector = vector,
                    Norm = MathF.Sqrt(Dot(vector, vector)),
                    NormalizedText = Normalize(row.TextContent)
                };
            }

            // Vân tay đọc TRƯỚC khi tải: nếu có ai ghi giữa chừng, lần sau vân tay lệch và ta tải lại — sai về
            // phía an toàn. Đọc sau thì có thể gán vân tay mới cho dữ liệu cũ.
            _snapshot = new Snapshot(model, fingerprint, entryList);
            return entryList;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<(int, int, long, long)> ReadFingerprintAsync(IAppDbContext db, string model, CancellationToken cancellationToken)
    {
        var row = await db.DocumentChunk.AsNoTracking()
            .Where(x => x.EmbeddingModel == model)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                MaxId = g.Max(x => x.Id),
                CourseSum = g.Sum(x => (long)(x.CourseId ?? 0)),
                LessonSum = g.Sum(x => (long)(x.LessonId ?? 0))
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? (0, 0, 0L, 0L) : (row.Count, row.MaxId, row.CourseSum, row.LessonSum);
    }

    /// <summary>Cosine giữa vector câu hỏi (đã biết chuẩn) và một đoạn, dùng SIMD.</summary>
    public static double Cosine(float[] query, float queryNorm, Entry entry)
    {
        if (query.Length == 0 || query.Length != entry.Vector.Length || queryNorm == 0 || entry.Norm == 0) return 0;
        return Dot(query, entry.Vector) / ((double)queryNorm * entry.Norm);
    }

    public static float Norm(float[] vector) => MathF.Sqrt(Dot(vector, vector));

    private static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        var i = 0;
        var width = Vector<float>.Count;
        if (a.Length >= width)
        {
            var acc = Vector<float>.Zero;
            for (; i <= a.Length - width; i += width)
                acc += new Vector<float>(a, i) * new Vector<float>(b, i);
            sum = Vector.Sum(acc);
        }
        for (; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    /// <summary>Chữ -> " a b c " (thường, NFC, chỉ chữ và số). Cùng quy tắc tách từ với phần chấm điểm từ khóa.</summary>
    public static string Normalize(string text)
        => $" {string.Join(' ', SplitWordList(text))} ";

    public static List<string> SplitWordList(string text)
        => [.. WordRegex.Matches(text.Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant())
            .Select(x => x.Value)];

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
}
