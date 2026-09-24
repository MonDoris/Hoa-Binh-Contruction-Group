// File: Application/Common/TextChunker.cs
using System.Text;
using System.Text.RegularExpressions;

namespace HbELearning.Application.Common;

/// <summary>
/// Cắt văn bản dài thành các đoạn để nhúng vector (embedding) cho kho tri thức AI.
///
/// Tách ra khỏi DocumentProcessingService khi chuyển sang Clean Architecture vì HAI nơi ở HAI LỚP khác
/// nhau cùng cần nó: CourseKnowledgeService (Application) và DocumentProcessingService (Infrastructure).
/// Trước đó lớp Application phải gọi ngược lên một lớp của Infrastructure, và cặp hằng số 500/100 bị chép
/// ở cả hai tệp — sửa một bên quên bên kia thì cùng một tài liệu cắt ra hai kiểu đoạn khác nhau tùy đường
/// nạp, mà không có dấu hiệu nào báo.
///
/// Toàn bộ lớp này là xử lý chuỗi thuần: không database, không mạng, không phụ thuộc gói ngoài.
/// </summary>
public static class TextChunker
{
    /// <summary>Số ký tự tối đa của một đoạn. Khoảng 500 ký tự (~80 chữ) đủ gọn để vector còn "sắc" ý nghĩa
    /// mà vẫn đủ dài để câu trả lời có ngữ cảnh.</summary>
    public const int MaxChunkLength = 500;

    /// <summary>Số ký tự lặp lại giữa hai đoạn liền nhau. Không có phần gối đầu này, một ý bị cắt đúng
    /// ranh giới đoạn sẽ mất nghĩa ở cả hai đoạn và không đoạn nào tìm ra được.</summary>
    public const int ChunkOverlapLength = 100;


    /// <summary>Cắt văn bản thành các đoạn không quá <paramref name="maxLength"/> ký tự, ưu tiên cắt tại hết câu —
    /// cắt cứng giữa từ làm vector mang nghĩa sai và câu trả lời bị cụt. Mỗi đoạn lặp lại
    /// <paramref name="overlapLength"/> ký tự cuối của đoạn trước để không mất ngữ cảnh tại chỗ nối.</summary>
    public static List<string> SplitIntoChunks(string text, int maxLength = MaxChunkLength, int overlapLength = ChunkOverlapLength)
    {
        var chunkList = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunkList;

        if (maxLength < 50) maxLength = 50;
        if (overlapLength >= maxLength || overlapLength < 0) overlapLength = maxLength / 5;

        var builder = new StringBuilder();

        // Chốt đoạn hiện tại và mở đoạn kế tiếp bằng phần đuôi gối đầu.
        void Flush()
        {
            var chunk = builder.ToString().Trim();
            builder.Clear();
            if (chunk.Length == 0) return;

            chunkList.Add(chunk);

            if (overlapLength > 0 && chunk.Length > overlapLength)
                builder.Append(TakeTailFromWordBoundary(chunk, overlapLength)).Append(' ');
        }

        foreach (var sentence in SplitIntoSentenceList(text))
        {
            // Câu dài hơn cả một đoạn (bảng biểu, danh sách không có dấu chấm câu): cắt cứng thành nhiều phần.
            if (sentence.Length > maxLength)
            {
                Flush();
                builder.Clear();   // không gối đầu vào phần cắt cứng

                for (int start = 0; start < sentence.Length; start += maxLength)
                {
                    var part = sentence.Substring(start, Math.Min(maxLength, sentence.Length - start)).Trim();
                    if (part.Length > 0) chunkList.Add(part);
                }
                continue;
            }

            if (builder.Length + sentence.Length + 1 > maxLength) Flush();

            builder.Append(sentence).Append(' ');
        }

        // Phần còn lại: bỏ qua nếu nó chỉ là phần gối đầu của đoạn cuối (không có câu mới nào được thêm).
        var rest = builder.ToString().Trim();
        if (rest.Length > 0 && !(chunkList.Count > 0 && chunkList[^1].EndsWith(rest, StringComparison.Ordinal)))
            chunkList.Add(rest);

        return chunkList;
    }

    /// <summary>Tách văn bản thành câu: theo dấu kết câu . ! ? : có khoảng trắng phía sau, hoặc theo dòng mới.</summary>
    private static IEnumerable<string> SplitIntoSentenceList(string text)
        => Regex.Split(text, @"(?<=[\.\!\?\:])\s+|\n+")
            .Select(x => x.Trim())
            .Where(x => x.Length > 0);

    /// <summary>Lấy khoảng <paramref name="length"/> ký tự cuối của đoạn nhưng bắt đầu từ đầu một từ (không cắt giữa từ).</summary>
    private static string TakeTailFromWordBoundary(string chunk, int length)
    {
        var tail = chunk[^length..];
        var spaceIndex = tail.IndexOf(' ');
        return spaceIndex > 0 ? tail[(spaceIndex + 1)..] : tail;
    }
}
