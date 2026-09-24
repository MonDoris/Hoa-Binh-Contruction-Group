// File: Application/Features/Scenarios/IScenarioService.cs

namespace HbELearning.Application.Features.Scenarios;

/// <summary>Các ca sử dụng của chức năng Scenario. Tầng API gọi qua cổng này (xem API/Controllers/ScenarioService.cs).</summary>
public interface IScenarioService
{
    Task<Result> GetByCourse(int courseId);

    /// <summary>Chi tiết mô phỏng kèm đáp án đúng/điểm. Chỉ ĐỌC nên mở cho cả Trưởng phòng/Giám Đốc để duyệt
    /// nội dung; mọi thao tác SỬA vẫn giữ RoleName.ContentAuthor.</summary>
    Task<Result> GetForManage(int id);

    Task<Result> Create(ScenarioSceneRequest request);

    Task<Result> Update(int id, ScenarioSceneRequest request);

    Task<Result> Delete(int id);

    Task<Result> AddNode(int sceneId, ScenarioNodeRequest request);

    Task<Result> UpdateNode(int nodeId, ScenarioNodeRequest request);

    Task<Result> DeleteNode(int nodeId);

    /// <summary>Đặt cỡ người cho cả cảnh.
    ///
    /// Tách riêng khỏi PUT sửa mô phỏng vì màn đặt nhân vật chỉ kéo một thanh trượt: bắt nó gửi lại đủ tiêu
    /// đề, mô tả, điểm đạt, tệp bối cảnh... chỉ để đổi một con số là mở đường cho việc vô tình ghi đè những
    /// trường mà màn hình đó không hề hiển thị.</summary>
    Task<Result> SetCharacterScale(int sceneId, float value);

    Task<Result> AddCharacter(int sceneId, ScenarioCharacterRequest request);

    Task<Result> UpdateCharacter(int characterId, ScenarioCharacterRequest request);

    Task<Result> DeleteCharacter(int characterId);

    /// <summary>Bắt đầu (hoặc tiếp tục) một lượt chơi. Không trả về đáp án đúng/điểm/giải thích.</summary>
    Task<Result> Start(int sceneId, int enrollmentId);

    /// <summary>Trả lời một điểm nguy cơ — cộng/trừ điểm ngay, trả về phản hồi đúng/sai tức thời.</summary>
    Task<Result> Answer(int attemptId, SubmitScenarioAnswerRequest request);

    /// <summary>Kết thúc lượt chơi, tính đạt/không đạt theo % trên MaxScore rồi cập nhật tiến độ/điểm xếp hạng.</summary>
    Task<Result> Finish(int attemptId);

    /// <summary>Xem lại kết quả một lượt chơi (kèm lựa chọn đúng và giải thích).</summary>
    Task<Result> GetResult(int attemptId);

    /// <summary>Lịch sử các lượt chơi của tôi trên một mô phỏng.</summary>
    Task<Result> GetMyAttempt(int sceneId, int enrollmentId);
}
