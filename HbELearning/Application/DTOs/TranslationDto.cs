// File: Application/DTOs/TranslationDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Một KHÓA dịch kèm câu chữ của tất cả ngôn ngữ, gộp từ nhiều dòng HBE_TRANSLATION.
///
/// Gộp theo khóa (chứ không trả từng dòng (khóa, ngôn ngữ) riêng) vì người dịch luôn làm việc theo cặp:
/// sửa "Bối cảnh 3D" thì phải sửa luôn "3D Scene" bên cạnh, để hai bản không lệch nghĩa. Trả rời hai dòng
/// thì màn hình phải tự ghép lại, mà ghép sai là hai ngôn ngữ trôi mỗi bên một kiểu.</summary>
/// <param name="Group">Tiền tố trước dấu chấm đầu tiên (VD "course-detail") — dùng để lọc theo màn hình.</param>
/// <param name="IsCustomized">Có ngôn ngữ nào đang khác bản xuất xưởng không.</param>
/// <param name="TextByLang">Câu chữ đang dùng, theo mã ngôn ngữ.</param>
/// <param name="DefaultTextByLang">Câu chữ xuất xưởng, để so sánh và khôi phục. Thiếu khóa nào nghĩa là
/// ngôn ngữ đó không có bản gốc (khóa do công ty tự thêm).</param>
public record TranslationRowDto(
    string ResourceKey,
    string Group,
    bool IsSystem,
    bool IsCustomized,
    IReadOnlyDictionary<string, string> TextByLang,
    IReadOnlyDictionary<string, string> DefaultTextByLang,
    string? Note,
    DateTime? UpdatedAt,
    string? UpdatedByName);

/// <summary>Một nhóm khóa dịch (theo tiền tố) kèm số khóa — đổ vào ô lọc "Màn hình".</summary>
public record TranslationGroupDto(string Group, int KeyCount, int CustomizedCount);

/// <summary>Thêm mới / sửa câu chữ của một khóa. <paramref name="TextByLang"/> chỉ cần chứa những ngôn ngữ
/// muốn ghi — ngôn ngữ không gửi lên thì giữ nguyên câu chữ cũ.</summary>
public record TranslationSaveRequest(string ResourceKey, Dictionary<string, string> TextByLang, string? Note);

/// <summary>Khôi phục một khóa hệ thống về đúng câu chữ xuất xưởng.</summary>
public record TranslationResetRequest(string ResourceKey);

/// <summary>Một ngôn ngữ giao diện, kèm số khóa đã có câu chữ.</summary>
/// <param name="IsSystem">Ngôn ngữ đi kèm phần mềm — không xóa được.</param>
public record TranslationLanguageDto(
    string Lang, string Label, int KeyCount, string LocaleTag, bool IsSystem, bool IsActive);

/// <summary>Thêm một ngôn ngữ giao diện mới.</summary>
/// <param name="CopyFromLang">Chép sẵn toàn bộ câu chữ của ngôn ngữ này sang ngôn ngữ mới để người dịch có
/// cái mà sửa dần. Để trống thì ngôn ngữ mới bắt đầu với bảng dịch rỗng — mọi màn hình sẽ rơi về chuỗi dự
/// phòng trong HTML cho tới khi có người dịch.</param>
public record LanguageCreateRequest(string Code, string Name, string? LocaleTag, string? CopyFromLang);

/// <summary>Sửa tên hiển thị / thẻ locale / trạng thái của một ngôn ngữ.</summary>
public record LanguageUpdateRequest(string Name, string? LocaleTag, bool IsActive);

/// <summary>Một câu chữ giao diện gửi cho AI dịch. Khóa đi kèm không phải để đối chiếu mà là NGỮ CẢNH:
/// nó cho model biết câu này nằm ở màn hình nào, nhờ đó chọn được đúng nghĩa cho những chữ ngắn đa nghĩa
/// ("Đóng", "Mở", "Bước").</summary>
/// <param name="ReferenceText">Cùng câu đó bằng tiếng Anh (bảng "en" đã dịch đủ), gửi kèm làm bản đối
/// chiếu: model nhỏ dịch từ tiếng Anh sang Thái/Nhật chuẩn hơn hẳn dịch thẳng từ tiếng Việt, và có hai bản
/// nguồn thì khó hiểu sai nghĩa. Null khi nguồn hoặc đích chính là tiếng Anh.</param>
public record UiTextToTranslateDto(string ResourceKey, string SourceText, string? ReferenceText = null);

/// <summary>Một cặp câu đã có người duyệt, đưa vào câu lệnh làm VÍ DỤ MẪU cho model bắt chước thuật ngữ và
/// văn phong — cách "dạy" model nội bộ mà không cần huấn luyện lại (máy không có GPU để huấn luyện).</summary>
public record UiTranslationExampleDto(string SourceText, string TargetText);

/// <summary>Nhờ AI dịch một lô khóa từ ngôn ngữ này sang ngôn ngữ khác.</summary>
/// <param name="ResourceKeyList">Các khóa cần dịch trong lượt này. Màn hình dịch tự cắt lô rồi gọi nhiều
/// lượt, thay vì ném cả nghìn khóa vào một yêu cầu — có vậy mới hiện được tiến độ và bấm dừng giữa chừng.</param>
/// <param name="Overwrite">true = dịch đè lên cả những dòng ĐÃ có bản dịch. Mặc định false: chỉ điền vào
/// chỗ còn trống, để bản dịch người thật đã soát không bị máy ghi đè.</param>
/// <param name="Preview">true = chỉ dịch rồi trả về, KHÔNG ghi vào cơ sở dữ liệu — dùng cho nút gợi ý dịch
/// từng dòng, nơi người dịch còn phải đọc lại rồi mới bấm Lưu.</param>
public record AiTranslateRequest(
    string SourceLang, string TargetLang, List<string> ResourceKeyList, bool Overwrite, bool Preview);

/// <summary>Kết quả một lô dịch bằng AI.</summary>
/// <param name="TextByKey">Câu đã dịch theo từng khóa — màn hình lấy thẳng cái này cập nhật vào bảng đang
/// xem, khỏi tải lại cả nghìn dòng sau mỗi lô.</param>
/// <param name="SkippedCount">Số khóa bỏ qua vì nguồn trống, hoặc vì đích đã có chữ mà không chọn ghi đè.</param>
/// <param name="FailedKeyList">Khóa mà AI không trả về bản dịch — để người dùng biết chỗ nào còn phải tự dịch.</param>
public record AiTranslateResponse(
    int TranslatedCount, int SkippedCount, IReadOnlyDictionary<string, string> TextByKey,
    IReadOnlyList<string> FailedKeyList, string Message);

/// <summary>Gửi CẢ danh sách khóa cần dịch một lần — máy chủ tự cắt lô và dịch nền (xem IAiTranslateJobQueue).</summary>
public record AiTranslateJobRequest(string SourceLang, string TargetLang, List<string> ResourceKeyList, bool Overwrite);

/// <summary>Một câu AI vừa dịch xong trong lượt dịch nền.</summary>
public record AiTranslateJobTextDto(string Key, string Text);

/// <summary>Tiến độ một lượt dịch nền.</summary>
/// <param name="Status">Queued | Running | Completed | Cancelled | Failed.</param>
/// <param name="DoneCount">Số khóa đã đi qua AI (kể cả khóa bỏ qua / AI không trả về).</param>
/// <param name="CurrentBatch">Lô đang dịch, đếm từ 1.</param>
/// <param name="TextCount">Tổng số câu đã dịch tới giờ — lần hỏi sau gửi lại làm "from".</param>
/// <param name="TextList">Các câu đã dịch kể từ vị trí "from" của lần hỏi này.</param>
/// <param name="Message">Lý do dừng khi Failed / Cancelled.</param>
public record AiTranslateJobDto(
    string Id, string SourceLang, string TargetLang, string Status,
    int TotalCount, int DoneCount, int TranslatedCount, int SkippedCount, int FailedCount,
    int BatchSize, int BatchCount, int CurrentBatch,
    int TextCount, IReadOnlyList<AiTranslateJobTextDto> TextList,
    string? Message, DateTime CreatedAt, DateTime? FinishedAt);
