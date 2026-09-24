// File: Application/Features/Scenarios/ScenarioService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Scenarios;

/// <summary>Mô phỏng tình huống công việc bằng 3D — mirror kiến trúc QuizService, khác ở 2 điểm:
/// (1) nộp lựa chọn từng điểm nguy cơ một thay vì nộp 1 lần cho cả bài, để phản hồi tức thời khi khám phá cảnh 3D;
/// (2) điểm có thể âm nên không giới hạn số lượt chơi/ngày như Quiz.</summary>
public class ScenarioService : IScenarioService
{
    private readonly IAppDbContext _db;
    private readonly ILearningService _learningService;
    private readonly ICurrentUser _currentUser;

    public ScenarioService(IAppDbContext db, ILearningService learningService, ICurrentUser currentUser)
    {
        _db = db;
        _learningService = learningService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetByCourse(int courseId)
    {
        var list = await _db.ScenarioScene.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new ScenarioSceneListDto(x.Id, x.CourseId, x.Title, x.PassScore,
                x.NodeList.Count, x.IsActive, x.LessonId, x.Lesson != null ? x.Lesson.Title : null,
                x.SceneKey, x.IsSequential, x.AssetType))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetForManage(int id)
    {
        var scene = await _db.ScenarioScene.AsNoTracking()
            .Include(x => x.Course)
            .Include(x => x.Lesson)
            .Include(x => x.NodeList).ThenInclude(n => n.ChoiceList)
            .Include(x => x.CharacterList)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (scene is null) return Result.NotFound(new MessageResponse("Không tìm thấy mô phỏng."));

        var nodeList = scene.NodeList.OrderBy(n => n.OrderIndex).Select(n => new ScenarioNodeManageDto(
            n.Id, n.Title, n.SituationText, n.PositionX, n.PositionY, n.PositionZ, n.OrderIndex, n.MediaUrl,
            n.ChoiceList.OrderBy(c => c.OrderIndex)
                .Select(c => new ScenarioChoiceManageDto(c.Id, c.Content, c.IsCorrect, c.PointDelta, c.Feedback, c.OrderIndex))
                .ToList(),
            n.AttachedCharacterId))
            .ToList();

        return Result.Success(new ScenarioManageDto(scene.Id, scene.CourseId, scene.Course.Title, scene.Title, scene.Description,
            scene.PassScore, scene.IsActive, scene.LessonId, scene.Lesson?.Title,
            scene.SceneKey, scene.IsSequential, scene.AssetUrl, scene.AssetType, nodeList,
            scene.CharacterScale, ToCharacterDtoList(scene.CharacterList, await LoadLibraryCharacterUrlMapAsync())));
    }

    public async Task<Result> Create(ScenarioSceneRequest request)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == request.CourseId))
            return Result.Invalid(new MessageResponse("Khóa học không tồn tại."));
        var sceneError = ValidateScene(request);
        if (sceneError is not null) return Result.Invalid(new MessageResponse(sceneError));

        var (assetUrl, assetType) = NormalizeAsset(request);
        var scene = new ScenarioScene
        {
            CourseId = request.CourseId,
            LessonId = request.LessonId,
            Title = request.Title.Trim(),
            Description = request.Description,
            PassScore = request.PassScore,
            IsActive = request.IsActive,
            SceneKey = request.SceneKey,
            AssetUrl = assetUrl,
            AssetType = assetType,
            IsSequential = request.IsSequential,
            CharacterScale = request.CharacterScale
        };

        _db.ScenarioScene.Add(scene);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse($"Đã tạo mô phỏng #{scene.Id}."));
    }

    public async Task<Result> Update(int id, ScenarioSceneRequest request)
    {
        var scene = await _db.ScenarioScene.FirstOrDefaultAsync(x => x.Id == id);
        if (scene is null) return Result.NotFound(new MessageResponse("Không tìm thấy mô phỏng."));
        var sceneError = ValidateScene(request);
        if (sceneError is not null) return Result.Invalid(new MessageResponse(sceneError));

        scene.LessonId = request.LessonId;
        scene.Title = request.Title.Trim();
        scene.Description = request.Description;
        scene.PassScore = request.PassScore;
        scene.IsActive = request.IsActive;
        scene.SceneKey = request.SceneKey;
        (scene.AssetUrl, scene.AssetType) = NormalizeAsset(request);
        scene.IsSequential = request.IsSequential;
        scene.CharacterScale = request.CharacterScale;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    public async Task<Result> Delete(int id)
    {
        var scene = await _db.ScenarioScene.FirstOrDefaultAsync(x => x.Id == id);
        if (scene is null) return Result.NotFound(new MessageResponse("Không tìm thấy mô phỏng."));

        if (await _db.ScenarioAttempt.AnyAsync(x => x.ScenarioSceneId == id))
        {
            scene.IsActive = false;
            await _db.SaveChangesAsync();
            return Result.Success(new MessageResponse("Mô phỏng đã có người chơi nên được ẩn thay vì xóa."));
        }

        var pathItemList = await _db.PathItem.Where(x => x.ScenarioSceneId == id).ToListAsync();
        _db.PathItem.RemoveRange(pathItemList);
        _db.ScenarioScene.Remove(scene);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa mô phỏng."));
    }

    public async Task<Result> AddNode(int sceneId, ScenarioNodeRequest request)
    {
        if (!await _db.ScenarioScene.AnyAsync(x => x.Id == sceneId))
            return Result.NotFound(new MessageResponse("Không tìm thấy mô phỏng."));

        var error = ValidateChoice(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        var maxOrder = await _db.ScenarioNode.Where(x => x.ScenarioSceneId == sceneId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        var node = new ScenarioNode
        {
            ScenarioSceneId = sceneId,
            Title = request.Title.Trim(),
            SituationText = request.SituationText.Trim(),
            PositionX = request.PositionX,
            PositionY = request.PositionY,
            PositionZ = request.PositionZ,
            MediaUrl = string.IsNullOrWhiteSpace(request.MediaUrl) ? null : request.MediaUrl.Trim(),
            OrderIndex = request.OrderIndex > 0 ? request.OrderIndex : maxOrder + 1,
            AttachedCharacterId = await ResolveAttachedCharacterIdAsync(sceneId, request.AttachedCharacterId)
        };

        var order = 1;
        foreach (var choice in request.ChoiceList)
        {
            node.ChoiceList.Add(new ScenarioChoice
            {
                Content = choice.Content.Trim(),
                IsCorrect = choice.IsCorrect,
                PointDelta = choice.PointDelta,
                Feedback = choice.Feedback,
                OrderIndex = choice.OrderIndex > 0 ? choice.OrderIndex : order
            });
            order++;
        }

        _db.ScenarioNode.Add(node);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse($"Đã thêm điểm nguy cơ #{node.Id}."));
    }

    public async Task<Result> UpdateNode(int nodeId, ScenarioNodeRequest request)
    {
        var node = await _db.ScenarioNode.Include(x => x.ChoiceList)
            .FirstOrDefaultAsync(x => x.Id == nodeId);
        if (node is null) return Result.NotFound(new MessageResponse("Không tìm thấy điểm nguy cơ."));

        var error = ValidateChoice(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        node.Title = request.Title.Trim();
        node.SituationText = request.SituationText.Trim();
        node.PositionX = request.PositionX;
        node.PositionY = request.PositionY;
        node.PositionZ = request.PositionZ;
        node.MediaUrl = string.IsNullOrWhiteSpace(request.MediaUrl) ? null : request.MediaUrl.Trim();
        node.OrderIndex = request.OrderIndex;
        node.AttachedCharacterId = await ResolveAttachedCharacterIdAsync(node.ScenarioSceneId, request.AttachedCharacterId);

        _db.ScenarioChoice.RemoveRange(node.ChoiceList);

        var order = 1;
        foreach (var choice in request.ChoiceList)
        {
            node.ChoiceList.Add(new ScenarioChoice
            {
                Content = choice.Content.Trim(),
                IsCorrect = choice.IsCorrect,
                PointDelta = choice.PointDelta,
                Feedback = choice.Feedback,
                OrderIndex = choice.OrderIndex > 0 ? choice.OrderIndex : order
            });
            order++;
        }

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật điểm nguy cơ thành công."));
    }

    public async Task<Result> DeleteNode(int nodeId)
    {
        var node = await _db.ScenarioNode.FirstOrDefaultAsync(x => x.Id == nodeId);
        if (node is null) return Result.NotFound(new MessageResponse("Không tìm thấy điểm nguy cơ."));

        if (await _db.ScenarioAttemptDetail.AnyAsync(x => x.ScenarioNodeId == nodeId))
            return Result.Invalid(new MessageResponse("Điểm nguy cơ đã được sử dụng trong lượt chơi, không thể xóa."));

        _db.ScenarioNode.Remove(node);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa điểm nguy cơ."));
    }

    // ---------- Nhân vật trong cảnh ----------

    /// <summary>Các tư thế hợp lệ — phải khớp POSE_LIST trong wwwroot/js/sim-characters.js, vốn lại khớp
    /// TÊN ĐOẠN ANIMATION trong tệp nhân vật (xem wwwroot/models/README.txt).
    ///
    /// Kiểm ở server chứ không chỉ ở giao diện: một tư thế gõ sai sẽ làm nhân vật đứng chết ở tư thế chữ T
    /// giữa cảnh, mà lỗi đó chỉ lộ ra lúc học viên vào học.</summary>
    /// <summary>Các bộ nhân vật hợp lệ — phải khớp CHARACTER_SET_LIST trong wwwroot/js/sim-characters.js.
    /// "staff" là key của dữ liệu cũ, giữ lại để những nhân vật đã đặt trước đó không bị coi là hỏng.</summary>
    public static readonly string[] CharacterSetList = { "office", "worker", "staff" };

    public static readonly string[] CharacterPoseList =
        { "idle", "stand", "sit", "walk", "drink", "eat", "talk", "type", "point", "phone" };

    public async Task<Result> SetCharacterScale(int sceneId, float value)
    {
        var scene = await _db.ScenarioScene.FirstOrDefaultAsync(x => x.Id == sceneId);
        if (scene is null) return Result.NotFound(new MessageResponse("Không tìm thấy mô phỏng."));
        if (value is <= 0.01f or > 100f) return Result.Invalid(new MessageResponse("Cỡ người không hợp lệ."));

        scene.CharacterScale = value;
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã lưu cỡ người của cảnh."));
    }

    public async Task<Result> AddCharacter(int sceneId, ScenarioCharacterRequest request)
    {
        if (!await _db.ScenarioScene.AnyAsync(x => x.Id == sceneId))
            return Result.NotFound(new MessageResponse("Không tìm thấy mô phỏng."));

        var error = await ValidateCharacterAsync(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        var maxOrder = await _db.ScenarioCharacter.Where(x => x.ScenarioSceneId == sceneId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        var character = new ScenarioCharacter { ScenarioSceneId = sceneId };
        ApplyCharacter(character, request, maxOrder + 1);

        _db.ScenarioCharacter.Add(character);
        await _db.SaveChangesAsync();

        // Trả về cả bản ghi chứ không chỉ câu thông báo: màn soạn bài phải dựng ngay nhân vật vừa thêm vào
        // cảnh 3D đang mở, mà muốn dựng thì cần Id để về sau còn sửa/xóa đúng người đó.
        return Result.Success(ToCharacterDto(character, await LoadLibraryCharacterUrlMapAsync()));
    }

    public async Task<Result> UpdateCharacter(int characterId, ScenarioCharacterRequest request)
    {
        var character = await _db.ScenarioCharacter.FirstOrDefaultAsync(x => x.Id == characterId);
        if (character is null) return Result.NotFound(new MessageResponse("Không tìm thấy nhân vật."));

        var error = await ValidateCharacterAsync(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        ApplyCharacter(character, request, character.OrderIndex);
        await _db.SaveChangesAsync();
        return Result.Success(ToCharacterDto(character, await LoadLibraryCharacterUrlMapAsync()));
    }

    public async Task<Result> DeleteCharacter(int characterId)
    {
        var character = await _db.ScenarioCharacter.FirstOrDefaultAsync(x => x.Id == characterId);
        if (character is null) return Result.NotFound(new MessageResponse("Không tìm thấy nhân vật."));

        // Tình huống đang gắn vào người này phải được gỡ ra TRƯỚC, nếu không nó trỏ vào một nhân vật không
        // còn tồn tại: lúc dựng cảnh sẽ không tìm ra chỗ đặt dấu chấm than và tình huống biến mất khỏi bài
        // học mà không ai được báo. Gỡ ra thì nó rơi về tọa độ cố định đã lưu sẵn — vẫn bấm được.
        var attachedNodeList = await _db.ScenarioNode
            .Where(x => x.AttachedCharacterId == characterId).ToListAsync();
        foreach (var node in attachedNodeList) node.AttachedCharacterId = null;

        _db.ScenarioCharacter.Remove(character);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse(attachedNodeList.Count == 0
            ? "Đã xóa nhân vật."
            : $"Đã xóa nhân vật. {attachedNodeList.Count} tình huống đang gắn vào người này chuyển về vị trí cố định."));
    }

    /// <summary>Tiền tố của bộ nhân vật do công ty tự tải lên Thư viện 3D: "asset:{id của SimAsset}".
    ///
    /// Dùng chung một cột CharacterKey cho cả bộ cài sẵn lẫn bộ tải lên, phân biệt bằng tiền tố, thay vì
    /// thêm một cột khóa ngoại riêng: hai thứ này luôn được đọc/ghi như một lựa chọn duy nhất trên giao diện,
    /// và bộ cài sẵn thì không có bản ghi nào trong bảng để mà trỏ tới.</summary>
    public const string LibraryCharacterPrefix = "asset:";

    /// <summary>Id của SimAsset trong một CharacterKey dạng "asset:12", hoặc null nếu không phải dạng đó.</summary>
    public static int? ParseLibraryCharacterId(string? characterKey) =>
        characterKey is not null && characterKey.StartsWith(LibraryCharacterPrefix)
            && int.TryParse(characterKey[LibraryCharacterPrefix.Length..], out var id)
                ? id
                : null;

    private async Task<string?> ValidateCharacterAsync(ScenarioCharacterRequest request)
    {
        if (!CharacterPoseList.Contains(request.PoseKey)) return "Tư thế không hợp lệ.";

        var libraryId = ParseLibraryCharacterId(request.CharacterKey);
        if (libraryId is not null)
        {
            // Phải kiểm đúng LOẠI, không chỉ kiểm tồn tại: trỏ vào một model nhà xưởng trong thư viện thì
            // lúc dựng cảnh sẽ mọc lên một tòa nhà đứng thay chỗ người, và không có thông báo lỗi nào.
            var exists = await _db.SimAsset.AsNoTracking().AnyAsync(x =>
                x.Id == libraryId && x.AssetType == SimAssetService.CharacterAssetType);
            if (!exists) return "Bộ nhân vật không có trong Thư viện 3D.";
        }
        else if (!CharacterSetList.Contains(request.CharacterKey ?? ""))
        {
            return "Bộ nhân vật không hợp lệ.";
        }

        // Màu đi thẳng vào thuộc tính CSS/vật liệu ở trình duyệt nên phải đúng dạng #rrggbb — nhận chuỗi tùy
        // ý ở đây là để lọt một giá trị mà lúc dựng cảnh mới lộ ra, dưới dạng nhân vật mất màu không rõ lý do.
        if (!string.IsNullOrWhiteSpace(request.OutfitColor)
            && !System.Text.RegularExpressions.Regex.IsMatch(request.OutfitColor, "^#[0-9a-fA-F]{6}$"))
            return "Màu trang phục phải có dạng #rrggbb.";

        // Lối đi chỉ có nghĩa với tư thế đi. Không chặn ở đây (người soạn bài hay vẽ lối đi trước rồi mới
        // đổi tư thế), nhưng JSON hỏng thì phải chặn: trình dựng cảnh đọc không ra sẽ ném lỗi giữa lúc
        // dựng và cả cảnh trắng xóa, chứ không chỉ mình nhân vật đó biến mất.
        if (!string.IsNullOrWhiteSpace(request.PathJson) && ParsePathPointCount(request.PathJson) < 0)
            return "Lối đi không hợp lệ.";

        return null;
    }

    /// <summary>Số điểm của lối đi, hoặc -1 nếu JSON không đọc được / sai cấu trúc.</summary>
    private static int ParsePathPointCount(string pathJson)
    {
        try
        {
            var pointList = System.Text.Json.JsonSerializer.Deserialize<List<List<float>>>(pathJson);
            if (pointList is null) return -1;
            return pointList.Any(p => p.Count != 3) ? -1 : pointList.Count;
        }
        catch (System.Text.Json.JsonException) { return -1; }
    }

    private static void ApplyCharacter(ScenarioCharacter character, ScenarioCharacterRequest request, int orderIndex)
    {
        character.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        character.CharacterKey = request.CharacterKey.Trim();
        character.OutfitColor = string.IsNullOrWhiteSpace(request.OutfitColor) ? null : request.OutfitColor.Trim().ToLowerInvariant();
        character.PoseKey = request.PoseKey.Trim();
        character.PositionX = request.PositionX;
        character.PositionY = request.PositionY;
        character.PositionZ = request.PositionZ;
        character.RotationY = request.RotationY;
        character.Scale = request.Scale;
        character.PathJson = string.IsNullOrWhiteSpace(request.PathJson) ? null : request.PathJson.Trim();
        character.WalkSpeed = request.WalkSpeed;
        character.OrderIndex = request.OrderIndex > 0 ? request.OrderIndex : orderIndex;
    }

    /// <param name="libraryUrlByAssetId">Đường dẫn tệp của các nhân vật trong Thư viện 3D. Bộ CÀI SẴN không
    /// có trong bảng này và trả về ModelUrl = null — trình dựng cảnh tự tra tệp của chúng trong
    /// CHARACTER_SET_LIST (js/sim-characters.js). Cố ý không chép bảng đó sang server: hai nơi cùng giữ một
    /// danh sách là sớm muộn cũng lệch nhau.</param>
    private static ScenarioCharacterDto ToCharacterDto(
        ScenarioCharacter x, IReadOnlyDictionary<int, string>? libraryUrlByAssetId = null)
    {
        var libraryId = ParseLibraryCharacterId(x.CharacterKey);
        var modelUrl = libraryId is not null && libraryUrlByAssetId is not null
            && libraryUrlByAssetId.TryGetValue(libraryId.Value, out var url) ? url : null;

        return new ScenarioCharacterDto(
            x.Id, x.Label, x.CharacterKey, x.PoseKey,
            x.PositionX, x.PositionY, x.PositionZ, x.RotationY, x.Scale,
            x.PathJson, x.WalkSpeed, x.OrderIndex, x.OutfitColor, modelUrl);
    }

    private static List<ScenarioCharacterDto> ToCharacterDtoList(
        IEnumerable<ScenarioCharacter> list, IReadOnlyDictionary<int, string>? libraryUrlByAssetId = null) =>
        list.OrderBy(x => x.OrderIndex).Select(x => ToCharacterDto(x, libraryUrlByAssetId)).ToList();

    /// <summary>Đường dẫn tệp của mọi nhân vật trong Thư viện 3D, tra theo Id. Thư viện nhân vật chỉ có vài
    /// mục nên lấy trọn một lượt rẻ hơn hẳn việc dò từng nhân vật đã đặt trong cảnh.</summary>
    private async Task<Dictionary<int, string>> LoadLibraryCharacterUrlMapAsync() =>
        await _db.SimAsset.AsNoTracking()
            .Where(x => x.AssetType == SimAssetService.CharacterAssetType)
            .ToDictionaryAsync(x => x.Id, x => x.FileUrl);

    /// <summary>Chỉ nhận nhân vật thuộc ĐÚNG cảnh này — gắn tình huống vào một nhân vật của mô phỏng khác
    /// thì lúc dựng cảnh không tìm ra người đó và tình huống mất chỗ đứng.</summary>
    private async Task<int?> ResolveAttachedCharacterIdAsync(int sceneId, int? characterId)
    {
        if (characterId is null) return null;
        var belongs = await _db.ScenarioCharacter
            .AnyAsync(x => x.Id == characterId && x.ScenarioSceneId == sceneId);
        return belongs ? characterId : null;
    }

    /// <summary>Các bối cảnh 3D dựng sẵn — phải khớp key khai báo trong wwwroot/js/sim-registry.js.
    /// Kiểm ở server để một key gõ sai không tạo ra mô phỏng vĩnh viễn không mở được cảnh nào.</summary>
    private static readonly string[] SceneKeyList = { "construction", "hr-office", "custom" };

    /// <summary>Các loại tệp bối cảnh mà wwwroot/js/sim-asset-scene.js biết dựng.</summary>
    private static readonly string[] AssetTypeList = { "glb", "pano-image", "pano-video", "image-2d" };

    /// <summary>Kiểm tra bối cảnh của một kịch bản. Với bối cảnh "custom" (nghiệp vụ tự tải tệp lên) thì
    /// tệp và loại tệp là BẮT BUỘC — thiếu một trong hai, trang mô phỏng chỉ dựng được một cảnh trống,
    /// và lỗi đó chỉ lộ ra khi học viên vào học chứ không phải lúc soạn bài.</summary>
    private static string? ValidateScene(ScenarioSceneRequest request)
    {
        if (!SceneKeyList.Contains(request.SceneKey)) return "Bối cảnh 3D không hợp lệ.";
        if (request.SceneKey != "custom") return null;

        if (string.IsNullOrWhiteSpace(request.AssetUrl))
            return "Bối cảnh tự tải lên phải có tệp nội dung: chọn một tệp từ Thư viện 3D hoặc tải lên "
                 + "model 3D (.glb/.gltf/.fbx/.obj), ảnh 360°, video 360° hoặc ảnh thường.";
        if (!AssetTypeList.Contains(request.AssetType ?? ""))
            return "Loại tệp bối cảnh không hợp lệ.";
        // Chỉ nhận đường dẫn nội bộ do UploadService sinh ra: tệp ngoài (http://...) vừa có thể chết link
        // vừa bị trình duyệt chặn vì khác nguồn khi Three.js đọc texture.
        if (!request.AssetUrl.StartsWith("/uploads/") && !request.AssetUrl.StartsWith("/models/"))
            return "Tệp bối cảnh phải là tệp đã tải lên hệ thống.";

        return null;
    }

    /// <summary>Bối cảnh dựng sẵn thì không giữ tệp tải lên — tránh việc đổi từ "custom" sang "công trường"
    /// mà bản ghi vẫn còn đường dẫn cũ, lần sau mở ra không hiểu đang dùng cái nào.</summary>
    private static (string? url, string? type) NormalizeAsset(ScenarioSceneRequest request) =>
        request.SceneKey == "custom" ? (request.AssetUrl?.Trim(), request.AssetType) : (null, null);

    /// <summary>Phải có đúng 1 lựa chọn đúng, và lựa chọn đó phải có điểm dương và lớn nhất — nếu không
    /// MaxScore (tổng điểm tối đa) của lượt chơi có thể ra 0/âm khiến việc tính đạt/không đạt vô nghĩa.</summary>
    private static string? ValidateChoice(ScenarioNodeRequest request)
    {
        var correctCount = request.ChoiceList.Count(x => x.IsCorrect);
        if (correctCount != 1) return "Phải có đúng một lựa chọn đúng.";

        var correctChoice = request.ChoiceList.First(x => x.IsCorrect);
        if (correctChoice.PointDelta <= 0) return "Lựa chọn đúng phải có điểm dương.";
        if (request.ChoiceList.Any(x => x.PointDelta > correctChoice.PointDelta))
            return "Lựa chọn đúng phải có điểm cao nhất trong các lựa chọn.";

        return null;
    }

    public async Task<Result> Start(int sceneId, int enrollmentId)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        var scene = await _db.ScenarioScene.AsNoTracking()
            .Include(x => x.NodeList).ThenInclude(n => n.ChoiceList)
            .FirstOrDefaultAsync(x => x.Id == sceneId && x.IsActive);

        if (scene is null) return Result.NotFound(new MessageResponse("Không tìm thấy mô phỏng."));
        if (scene.CourseId != enrollment.CourseId)
            return Result.Invalid(new MessageResponse("Mô phỏng không thuộc khóa học này."));
        if (scene.NodeList.Count == 0)
            return Result.Invalid(new MessageResponse("Mô phỏng chưa có điểm nguy cơ nào."));

        // Kiểm tra mở khóa theo lộ trình
        var pathList = await _learningService.BuildPathProgressAsync(enrollmentId);
        var step = pathList.FirstOrDefault(x => x.ScenarioSceneId == sceneId);
        if (step is not null && !step.IsUnlocked)
            return Result.Invalid(new MessageResponse("Bạn cần hoàn thành các bước trước đó trong lộ trình."));

        // Không giới hạn số lượt chơi (khác Quiz) — tiếp tục lượt InProgress gần nhất nếu có, chưa thì tạo mới.
        var openAttempt = await _db.ScenarioAttempt.Include(x => x.DetailList)
            .Where(x => x.EnrollmentId == enrollmentId && x.ScenarioSceneId == sceneId
                     && x.Status == ScenarioAttemptStatus.InProgress)
            .OrderByDescending(x => x.AttemptNo)
            .FirstOrDefaultAsync();

        ScenarioAttempt attempt;
        if (openAttempt is not null)
        {
            attempt = openAttempt;
        }
        else
        {
            var totalAttempt = await _db.ScenarioAttempt
                .CountAsync(x => x.EnrollmentId == enrollmentId && x.ScenarioSceneId == sceneId);

            attempt = new ScenarioAttempt
            {
                EnrollmentId = enrollmentId,
                ScenarioSceneId = sceneId,
                AttemptNo = totalAttempt + 1,
                StartedAt = DateTime.Now,
                MaxScore = scene.NodeList.Sum(n => n.ChoiceList.Count > 0 ? n.ChoiceList.Max(c => c.PointDelta) : 0)
            };
            _db.ScenarioAttempt.Add(attempt);
            await _db.SaveChangesAsync();
        }

        var answeredNodeIdList = attempt.DetailList?.Select(d => d.ScenarioNodeId).ToList() ?? new List<int>();

        // Tên bối cảnh do Thư viện 3D quản (sửa được trên giao diện) — trả kèm để trang học viên không phải
        // dùng chuỗi cứng trong js/sim-registry.js. Null với "custom" vì bối cảnh tự tải lên không có ở đó.
        var sceneName = await _db.SimBuiltinScene.AsNoTracking()
            .Where(x => x.SceneKey == scene.SceneKey)
            .Select(x => x.Name)
            .FirstOrDefaultAsync();

        var nodeList = scene.NodeList.OrderBy(n => n.OrderIndex).Select(n => new ScenarioNodeTakeDto(
            n.Id, n.Title, n.SituationText, n.OrderIndex, n.PositionX, n.PositionY, n.PositionZ, n.MediaUrl,
            n.ChoiceList.OrderBy(c => c.OrderIndex)
                .Select(c => new ScenarioChoiceTakeDto(c.Id, c.Content)).ToList(),
            n.AttachedCharacterId))
            .ToList();

        // Nhân vật trong cảnh — học viên phải nhìn thấy đúng cảnh mà người soạn bài đã dựng. Truy vấn riêng
        // thay vì .Include vào scene ở trên: scene đã kéo theo NodeList và ChoiceList, thêm một nhánh con
        // nữa là EF nhân chéo các bảng và trả về hàng nghìn dòng trùng cho một cảnh chỉ có chục người.
        var characterList = await _db.ScenarioCharacter.AsNoTracking()
            .Where(x => x.ScenarioSceneId == sceneId)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync();

        return Result.Success(new ScenarioStartDto(attempt.Id, scene.Id, scene.Title, scene.Description, attempt.AttemptNo,
            scene.PassScore, attempt.MaxScore, attempt.Score, attempt.StartedAt,
            scene.SceneKey, scene.IsSequential, scene.AssetUrl, scene.AssetType, nodeList, answeredNodeIdList,
            sceneName, scene.CharacterScale,
            ToCharacterDtoList(characterList, await LoadLibraryCharacterUrlMapAsync())));
    }

    public async Task<Result> Answer(int attemptId, SubmitScenarioAnswerRequest request)
    {
        var attempt = await _db.ScenarioAttempt
            .Include(x => x.Enrollment)
            .Include(x => x.DetailList)
            .FirstOrDefaultAsync(x => x.Id == attemptId);

        if (attempt is null) return Result.NotFound(new MessageResponse("Không tìm thấy lượt chơi."));
        if (attempt.Enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
        if (attempt.Status == ScenarioAttemptStatus.Completed)
            return Result.Invalid(new MessageResponse("Lượt chơi này đã kết thúc."));
        if (attempt.DetailList.Any(d => d.ScenarioNodeId == request.NodeId))
            return Result.Invalid(new MessageResponse("Điểm nguy cơ này đã được xử lý trong lượt chơi hiện tại."));

        var node = await _db.ScenarioNode.Include(x => x.ChoiceList)
            .FirstOrDefaultAsync(x => x.Id == request.NodeId && x.ScenarioSceneId == attempt.ScenarioSceneId);
        if (node is null) return Result.NotFound(new MessageResponse("Không tìm thấy điểm nguy cơ."));

        // Mô phỏng QUY TRÌNH (IsSequential): chỉ cho xử lý bước chưa làm có OrderIndex nhỏ nhất. Giao diện đã
        // khóa sẵn các bước sau, nhưng phải chặn cả ở đây — nếu không, gọi thẳng API là bỏ qua được thứ tự,
        // mà thứ tự chính là NỘI DUNG cần học của một quy trình (VD không thể ký hợp đồng trước khi phỏng vấn).
        if (await _db.ScenarioScene.AsNoTracking()
                .Where(x => x.Id == attempt.ScenarioSceneId).Select(x => x.IsSequential).FirstAsync())
        {
            var answeredIdList = attempt.DetailList.Select(d => d.ScenarioNodeId).ToList();
            var nextNode = await _db.ScenarioNode.AsNoTracking()
                .Where(x => x.ScenarioSceneId == attempt.ScenarioSceneId && !answeredIdList.Contains(x.Id))
                .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (nextNode is not null && nextNode.Id != node.Id)
                return Result.Invalid(new MessageResponse($"Mô phỏng này phải làm theo thứ tự — bước tiếp theo là \"{nextNode.Title}\"."));
        }

        var choice = node.ChoiceList.FirstOrDefault(x => x.Id == request.ChoiceId);
        if (choice is null) return Result.NotFound(new MessageResponse("Không tìm thấy lựa chọn."));

        var correctChoice = node.ChoiceList.First(x => x.IsCorrect);

        attempt.Score += choice.PointDelta; // Không giới hạn dưới — điểm có thể xuống âm.
        _db.ScenarioAttemptDetail.Add(new ScenarioAttemptDetail
        {
            ScenarioAttemptId = attempt.Id,
            ScenarioNodeId = node.Id,
            ScenarioChoiceId = choice.Id,
            PointsEarned = choice.PointDelta
        });

        await _db.SaveChangesAsync();

        return Result.Success(new ScenarioAnswerResultDto(node.Id, choice.Id, choice.IsCorrect,
            choice.PointDelta, attempt.Score, choice.Feedback, correctChoice.Id));
    }

    public async Task<Result> Finish(int attemptId)
    {
        var attempt = await _db.ScenarioAttempt
            .Include(x => x.Enrollment)
            .Include(x => x.ScenarioScene)
            .FirstOrDefaultAsync(x => x.Id == attemptId);

        if (attempt is null) return Result.NotFound(new MessageResponse("Không tìm thấy lượt chơi."));
        if (attempt.Enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
        if (attempt.Status == ScenarioAttemptStatus.Completed)
            return await GetResult(attemptId);

        // % trên MaxScore, KHÔNG so trực tiếp Score >= PassScore — Score là điểm thô có thể âm/vượt 100.
        attempt.IsPassed = attempt.MaxScore > 0 && (attempt.Score * 100m / attempt.MaxScore) >= attempt.ScenarioScene.PassScore;
        attempt.Status = ScenarioAttemptStatus.Completed;
        attempt.CompletedAt = DateTime.Now;

        await _db.SaveChangesAsync();

        // Cập nhật tiến độ khóa học + cộng điểm xếp hạng (idempotent) nếu đủ điều kiện
        await _learningService.RecalculateAsync(attempt.EnrollmentId);

        return await GetResult(attemptId);
    }

    public async Task<Result> GetResult(int attemptId)
    {
        var attempt = await _db.ScenarioAttempt.AsNoTracking()
            .Include(x => x.Enrollment)
            .Include(x => x.ScenarioScene)
            .Include(x => x.DetailList).ThenInclude(d => d.ScenarioNode)
            .Include(x => x.DetailList).ThenInclude(d => d.ScenarioChoice)
            .FirstOrDefaultAsync(x => x.Id == attemptId);

        if (attempt is null) return Result.NotFound(new MessageResponse("Không tìm thấy lượt chơi."));
        if (!_currentUser.IsManager && attempt.Enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        var itemList = attempt.DetailList.OrderBy(d => d.ScenarioNode.OrderIndex).Select(d =>
            new ScenarioResultItemDto(d.ScenarioNodeId, d.ScenarioNode.Title, d.ScenarioChoiceId,
                d.ScenarioChoice.Content, d.ScenarioChoice.IsCorrect, d.PointsEarned, d.ScenarioChoice.Feedback))
            .ToList();

        var scorePercent = attempt.MaxScore > 0 ? Math.Round(attempt.Score * 100m / attempt.MaxScore, 2) : 0m;

        return Result.Success(new ScenarioResultDto(attempt.Id, attempt.ScenarioSceneId, attempt.ScenarioScene.Title,
            attempt.AttemptNo, attempt.Score, attempt.MaxScore, scorePercent, attempt.ScenarioScene.PassScore,
            attempt.IsPassed, attempt.StartedAt, attempt.CompletedAt, itemList));
    }

    public async Task<Result> GetMyAttempt(int sceneId, int enrollmentId)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (!_currentUser.IsManager && enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        var list = await _db.ScenarioAttempt.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollmentId && x.ScenarioSceneId == sceneId)
            .OrderByDescending(x => x.AttemptNo)
            .Select(x => new { x.Id, x.AttemptNo, x.Score, x.MaxScore, x.IsPassed, x.Status, x.StartedAt, x.CompletedAt })
            .ToListAsync();

        return Result.Success(list);
    }
}
