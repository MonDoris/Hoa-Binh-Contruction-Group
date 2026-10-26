// File: Application/Features/OnlineClassRooms/RecordingSeekMap.cs

namespace HbELearning.Application.Features.OnlineClassRooms;

/// <summary>Một Bản ghi, với những gì bản đồ cần biết về nó.</summary>
public sealed record SeekRecording(int Id, DateTime CreatedAt, DateTime? StoppedAt);

/// <summary>Một mốc ghi hình trong Diễn biến (bắt đầu, tạm dừng, tiếp tục, dừng) gắn với một Bản ghi.</summary>
public sealed record SeekMark(int RecordingId, OnlineClassRoomEventType Type, DateTime At);

/// <summary>Vị trí để nhảy tới: Bản ghi nào, và bao nhiêu mili giây tính từ đầu video của Bản ghi đó.</summary>
public sealed record SeekTarget(int RecordingId, long OffsetMs);

/// <summary>
/// Từ một thời điểm trong Buổi học suy ra vị trí tương ứng trong video: thời điểm đó trừ lúc bắt đầu Bản ghi, rồi trừ
/// tổng các đoạn tạm dừng đã kết thúc trước nó (đoạn tạm dừng không vào video nên video "co" lại bấy nhiêu).
///
/// Thuần tuý, không I/O. Cách neo:
///  • Gốc của video là lúc đồng hồ ghi hình chạy (mốc "bắt đầu" trong Diễn biến) chứ không phải lúc tạo dòng Bản ghi —
///    dòng Bản ghi được tạo trước khi trình duyệt thật sự quay vài trăm mili giây. Bản ghi cũ không có mốc nào thì
///    mới phải lấy lúc tạo dòng Bản ghi.
///  • Bản ghi chưa có mốc "dừng" (cũ, hoặc bị cắt đột ngột) kết thúc khi Bản ghi kế tiếp bắt đầu, hoặc khi Buổi học kết thúc.
///  • Sự kiện nằm trong một đoạn tạm dừng, hay ngoài mọi Bản ghi, thì không nhảy được (trả null). Chính dòng "tạm dừng"
///    và "tiếp tục" thì nhảy được, tới đúng điểm cắt.
/// </summary>
public sealed class RecordingSeekMap
{
    private sealed record Span(int RecordingId, DateTime Start, DateTime End, IReadOnlyList<(DateTime From, DateTime To)> Pauses);

    private readonly List<Span> _spanList = new();

    public RecordingSeekMap(IEnumerable<SeekRecording> recordings, IEnumerable<SeekMark> marks, DateTime? sessionEnd)
    {
        var markList = marks.ToList();
        var recordingList = recordings.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();

        for (var i = 0; i < recordingList.Count; i++)
        {
            var recording = recordingList[i];
            var mine = markList.Where(m => m.RecordingId == recording.Id).OrderBy(m => m.At).ToList();

            var start = mine.FirstOrDefault(m => m.Type == OnlineClassRoomEventType.RecordingStarted)?.At ?? recording.CreatedAt;
            var stopMark = mine.FirstOrDefault(m => m.Type == OnlineClassRoomEventType.RecordingStopped)?.At;
            var end = stopMark ?? recording.StoppedAt
                ?? (i + 1 < recordingList.Count ? recordingList[i + 1].CreatedAt : sessionEnd ?? DateTime.MaxValue);

            var pauseList = new List<(DateTime From, DateTime To)>();
            DateTime? pausedSince = null;
            foreach (var mark in mine)
            {
                if (mark.Type == OnlineClassRoomEventType.RecordingPaused && pausedSince is null) pausedSince = mark.At;
                else if (mark.Type == OnlineClassRoomEventType.RecordingResumed && pausedSince is { } from)
                {
                    pauseList.Add((from, mark.At));
                    pausedSince = null;
                }
            }
            // Dừng khi đang tạm dừng: đoạn tạm dừng kéo dài tới lúc dừng — video đã kết thúc ở điểm cắt.
            if (pausedSince is { } open) pauseList.Add((open, end));

            _spanList.Add(new Span(recording.Id, start, end, pauseList));
        }
    }

    /// <summary>Vị trí video tương ứng với <paramref name="at"/>; null nếu thời điểm đó không có trong video nào.</summary>
    public SeekTarget? Locate(DateTime at)
    {
        // Nhiều Bản ghi chồng nhau (dữ liệu bất thường) thì lấy cái bắt đầu muộn nhất — Bản ghi mới nhất đang chạy.
        var span = _spanList.Where(s => s.Start <= at && at <= s.End).OrderByDescending(s => s.Start).FirstOrDefault();
        if (span is null) return null;

        // Nằm TRONG đoạn tạm dừng (không tính hai đầu) thì video không có hình ảnh của lúc này.
        if (span.Pauses.Any(p => p.From < at && at < p.To)) return null;

        var pausedBefore = span.Pauses.Where(p => p.To <= at).Sum(p => (p.To - p.From).Ticks);
        var offset = TimeSpan.FromTicks(Math.Max(0, (at - span.Start).Ticks - pausedBefore));
        return new SeekTarget(span.RecordingId, (long)Math.Round(offset.TotalMilliseconds));
    }
}
