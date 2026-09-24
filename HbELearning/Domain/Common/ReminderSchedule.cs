// File: Domain/Common/ReminderSchedule.cs
// Tách ra từ Infrastructure/BackgroundJobs/StudyReminderWorker.cs khi chuyển sang Clean Architecture.
//
// Đây là quy tắc nghiệp vụ thuần (chu kỳ lặp của một lời nhắc), không phải chi tiết hạ tầng — và nó được
// dùng ở HAI LỚP: StudyReminderWorker (Infrastructure) khi tới giờ bắn, và StudyReminderService (API)
// khi người dùng đặt một giờ đã trôi qua trong ngày. Để nó nằm trong tệp của background worker thì tầng
// API phải với vào Infrastructure chỉ để mượn một phép tính ngày.

namespace HbELearning.Domain.Common;

/// <summary>Tính mốc bắn kế tiếp của một lời nhắc lặp lại. Tách riêng khỏi dịch vụ nền để controller dùng
/// chung đúng một cách hiểu chu kỳ khi người dùng đặt giờ đã trôi qua trong ngày.</summary>
public static class ReminderSchedule
{
    /// <summary>Lần bắn đầu tiên SAU mốc <paramref name="after"/>, tính từ <paramref name="current"/> theo chu kỳ.
    /// Trả null với lời nhắc một lần (không có lần sau).</summary>
    public static DateTime? Next(DateTime current, ReminderRepeat repeat, DateTime after)
    {
        if (repeat == ReminderRepeat.None) return null;

        var next = current;
        // Nhảy từng chu kỳ cho tới khi vượt qua mốc: máy chủ tắt vài ngày thì lời nhắc hằng ngày phải quay
        // về đúng giờ đó của ngày SẮP TỚI, chứ không phải bắn bù mấy chục lần cho những ngày đã qua.
        do
        {
            next = repeat switch
            {
                ReminderRepeat.Daily => next.AddDays(1),
                ReminderRepeat.Weekly => next.AddDays(7),
                ReminderRepeat.Weekdays => NextWeekday(next),
                _ => next.AddDays(1)
            };
        }
        while (next <= after);

        return next;
    }

    /// <summary>Ngày làm việc kế tiếp cùng giờ — thứ Bảy/Chủ Nhật bị bỏ qua.</summary>
    private static DateTime NextWeekday(DateTime from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) next = next.AddDays(1);
        return next;
    }
}
