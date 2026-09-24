// File: Application/Features/SimAssets/SimAssetService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.SimAssets;

/// <summary>Thư viện bối cảnh 3D dùng chung: tải model .glb / ảnh 360° / video 360° / ảnh phẳng lên MỘT lần
/// rồi mọi mô phỏng của mọi khóa học chọn lại từ đây (xem <see cref="SimAsset"/>).
///
/// Cùng nhóm quyền "course" với ScenarioService vì đây là kho tư liệu soạn bài, không phải cấu hình hệ
/// thống — Trưởng Nhóm soạn được mô phỏng thì cũng phải góp được tệp bối cảnh vào thư viện.</summary>
public class SimAssetService : ISimAssetService
{
    /// <summary>Thư mục lưu tệp thư viện trong wwwroot/uploads — tách riêng khỏi "scenario" (tệp dùng cho
    /// đúng một mô phỏng) để nhìn thư mục là biết tệp nào dùng chung, tệp nào dùng riêng.</summary>
    private const string LibrarySubFolder = "sim-library";

    /// <summary>Loại tệp hợp lệ — phải khớp AssetTypeList trong <see cref="ScenarioService"/>, vì tệp chọn
    /// từ thư viện được gán thẳng vào ScenarioScene.AssetType.</summary>
    private static readonly string[] AssetTypeList = { "glb", "pano-image", "pano-video", "image-2d" };

    /// <summary>Loại "nhân vật": model người có xương để đặt vào cảnh, KHÔNG phải một bối cảnh.
    ///
    /// Cố ý nằm ngoài <see cref="AssetTypeList"/>: giá trị trong danh sách đó được gán thẳng vào
    /// ScenarioScene.AssetType, mà chọn một hình người làm "căn phòng" thì mô phỏng chỉ còn một người khổng
    /// lồ lơ lửng giữa hư không. Nhờ tách ra, nhân vật vẫn nằm chung thư viện (tải lên một lần, mọi mô phỏng
    /// dùng lại) nhưng không bao giờ lọt vào ô chọn bối cảnh.</summary>
    public const string CharacterAssetType = "character";

    /// <summary>Những loại người dùng TẢI LÊN được — gồm cả nhân vật, khác với danh sách gán cho bối cảnh.</summary>
    private static readonly string[] UploadableAssetTypeList = [.. AssetTypeList, CharacterAssetType];

    /// <summary>Loại quy ước của bối cảnh dựng sẵn trong danh sách thư viện. KHÔNG nằm trong
    /// <see cref="AssetTypeList"/>: đây không phải một loại tệp và không ai tải lên được nó — chỉ là nhãn để
    /// giao diện lọc và hiển thị. Phải khớp giá trị 'builtin' dùng trong wwwroot/pages/sim-library.html.</summary>
    private const string BuiltinAssetType = "builtin";

    /// <summary>Đuôi tệp chấp nhận cho từng loại. Kiểm ở đây chứ không chỉ dựa vào Upload:AllowedExtension:
    /// một file .mp4 tải lên rồi khai là "glb" vẫn qua được vòng kiểm định dạng chung, nhưng tới lúc học viên
    /// mở mô phỏng thì Three.js mới báo lỗi — sai kiểu này phải chặn ngay lúc tải lên.</summary>
    private static readonly Dictionary<string, string[]> ExtensionByAssetType = new()
    {
        ["glb"] = [".glb", ".gltf", ".fbx", ".obj"],
        ["pano-image"] = [".jpg", ".jpeg", ".png", ".webp"],
        ["pano-video"] = [".mp4", ".webm"],
        ["image-2d"] = [".jpg", ".jpeg", ".png", ".webp", ".gif"],
        // Nhân vật chỉ nhận .glb/.gltf, hẹp hơn bối cảnh: trình dựng nhân vật đọc bằng GLTFLoader và cần
        // đọc được bộ xương cùng các đoạn animation trong tệp (xem js/sim-characters.js). Tệp .fbx/.obj
        // tải lên sẽ qua vòng kiểm nhưng tới lúc dựng cảnh mới hỏng, lúc đó không ai biết vì sao.
        [CharacterAssetType] = [".glb", ".gltf"],
    };

    private readonly IAppDbContext _db;
    private readonly IFileService _fileService;
    private readonly IAppPaths _env;
    private readonly ILogger<SimAssetService> _logger;
    private readonly ICurrentUser _currentUser;

    public SimAssetService(IAppDbContext db, IFileService fileService, IAppPaths env, ILogger<SimAssetService> logger, ICurrentUser currentUser)
    {
        _db = db;
        _fileService = fileService;
        _env = env;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<Result> GetList(string? assetType, string? keyword, CancellationToken cancellationToken)
    {
        var loweredKeyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim().ToLower();

        // ----- Bối cảnh dựng sẵn -----
        // Lọc theo loại: chúng không mang loại tệp nào nên chỉ lọt qua khi không lọc, hoặc lọc đúng "builtin".
        var builtinItemList = new List<SimAssetListItemDto>();
        if (string.IsNullOrWhiteSpace(assetType) || assetType == BuiltinAssetType)
        {
            var builtinQuery = _db.SimBuiltinScene.AsNoTracking().Where(x => x.IsActive);
            if (loweredKeyword is not null)
                builtinQuery = builtinQuery.Where(x => x.Name.ToLower().Contains(loweredKeyword)
                                                    || (x.Category != null && x.Category.ToLower().Contains(loweredKeyword)));

            builtinItemList = await builtinQuery
                .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
                .Select(x => new SimAssetListItemDto(
                    x.Id, x.Name, x.Description, x.Category, BuiltinAssetType, null, null,
                    0, x.ThumbnailUrl, null, x.CreatedAt,
                    // Mô phỏng dùng bối cảnh dựng sẵn thì đối chiếu theo SceneKey, KHÔNG theo đường dẫn tệp
                    // như tệp tải lên — chúng không có tệp nào để trỏ tới.
                    _db.ScenarioScene.Count(s => s.SceneKey == x.SceneKey),
                    x.SceneKey))
                .ToListAsync(cancellationToken);
        }

        // ----- Tệp tải lên -----
        if (assetType == BuiltinAssetType) return Result.Success(builtinItemList);

        var query = _db.SimAsset.AsNoTracking().Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(assetType)) query = query.Where(x => x.AssetType == assetType);
        if (loweredKeyword is not null)
        {
            query = query.Where(x => x.Name.ToLower().Contains(loweredKeyword)
                                  || (x.Category != null && x.Category.ToLower().Contains(loweredKeyword)));
        }

        var itemList = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new SimAssetListItemDto(
                x.Id, x.Name, x.Description, x.Category, x.AssetType, x.FileUrl, x.OriginalFileName,
                x.FileSizeByte, x.ThumbnailUrl, x.CreatedBy != null ? x.CreatedBy.FullName : null, x.CreatedAt,
                _db.ScenarioScene.Count(s => s.AssetUrl == x.FileUrl), null))
            .ToListAsync(cancellationToken);

        return Result.Success(builtinItemList.Concat(itemList));
    }

    public async Task<Result> Upload(FileUpload file, string name, string assetType, string? description, string? category, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn tệp."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Invalid(new MessageResponse("Vui lòng đặt tên cho tệp trong thư viện."));
        if (!UploadableAssetTypeList.Contains(assetType))
            return Result.Invalid(new MessageResponse("Loại tệp không hợp lệ."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ExtensionByAssetType[assetType].Contains(extension))
            return Result.Invalid(new MessageResponse(
                $"Loại \"{AssetTypeLabel(assetType)}\" chỉ nhận tệp {string.Join(", ", ExtensionByAssetType[assetType])} — tệp bạn chọn là {extension}."));

        UploadResultDto saved;
        try
        {
            saved = await _fileService.SaveAsync(file, LibrarySubFolder);
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(new MessageResponse(ex.Message));
        }

        var asset = new SimAsset
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            AssetType = assetType,
            FileUrl = saved.Url,
            OriginalFileName = file.FileName,
            FileSizeByte = file.Length,
            CreatedById = _currentUser.UserId
        };

        _db.SimAsset.Add(asset);
        await _db.SaveChangesAsync(cancellationToken);

        var creatorName = await _db.User.AsNoTracking()
            .Where(x => x.Id == _currentUser.UserId).Select(x => x.FullName).FirstOrDefaultAsync(cancellationToken);

        return Result.Success(new SimAssetListItemDto(asset.Id, asset.Name, asset.Description, asset.Category, asset.AssetType,
            asset.FileUrl, asset.OriginalFileName, asset.FileSizeByte, asset.ThumbnailUrl, creatorName, asset.CreatedAt, 0));
    }

    public async Task<Result> Update(int id, SimAssetUpdateRequest request, CancellationToken cancellationToken)
    {
        var asset = await _db.SimAsset.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset is null) return Result.NotFound(new MessageResponse("Không tìm thấy tệp trong thư viện."));
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Invalid(new MessageResponse("Tên tệp trong thư viện không được để trống."));

        asset.Name = request.Name.Trim();
        asset.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        asset.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(new MessageResponse("Đã cập nhật thông tin tệp."));
    }

    public async Task<Result> SaveThumbnail(int id, SimAssetThumbnailRequest request, CancellationToken cancellationToken)
    {
        var asset = await _db.SimAsset.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset is null) return Result.NotFound(new MessageResponse("Không tìm thấy tệp trong thư viện."));

        var (thumbnailUrl, error) = await SaveThumbnailFileAsync(request.DataUrl, cancellationToken);
        if (thumbnailUrl is null) return Result.Invalid(new MessageResponse(error!));

        var previousUrl = asset.ThumbnailUrl;
        asset.ThumbnailUrl = thumbnailUrl;
        await _db.SaveChangesAsync(cancellationToken);

        // Chụp lại ảnh xem trước nhiều lần là chuyện thường; không dọn bản cũ thì thư mục phình dần bằng
        // những ảnh không còn bản ghi nào trỏ tới.
        if (!string.IsNullOrWhiteSpace(previousUrl)) _fileService.Delete(previousUrl);

        return Result.Success(new MessageResponse("Đã lưu ảnh xem trước."));
    }

    public async Task<Result> UpdateBuiltin(int id, SimAssetUpdateRequest request, CancellationToken cancellationToken)
    {
        var scene = await _db.SimBuiltinScene.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (scene is null) return Result.NotFound(new MessageResponse("Không tìm thấy bối cảnh dựng sẵn."));
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Invalid(new MessageResponse("Tên bối cảnh không được để trống."));

        scene.Name = request.Name.Trim();
        scene.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        scene.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(new MessageResponse("Đã cập nhật thông tin bối cảnh."));
    }

    public async Task<Result> SaveBuiltinThumbnail(int id, SimAssetThumbnailRequest request, CancellationToken cancellationToken)
    {
        var scene = await _db.SimBuiltinScene.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (scene is null) return Result.NotFound(new MessageResponse("Không tìm thấy bối cảnh dựng sẵn."));

        var (thumbnailUrl, error) = await SaveThumbnailFileAsync(request.DataUrl, cancellationToken);
        if (thumbnailUrl is null) return Result.Invalid(new MessageResponse(error!));

        var previousUrl = scene.ThumbnailUrl;
        scene.ThumbnailUrl = thumbnailUrl;
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousUrl)) _fileService.Delete(previousUrl);

        return Result.Success(new MessageResponse("Đã lưu ảnh xem trước."));
    }

    /// <summary>Kiểm và ghi ảnh xem trước xuống đĩa, trả về đường dẫn trong wwwroot; trả (null, lý do) nếu
    /// dữ liệu gửi lên không phải một ảnh hợp lệ. Dùng chung cho cả tệp tải lên lẫn bối cảnh dựng sẵn.</summary>
    private async Task<(string? Url, string? Error)> SaveThumbnailFileAsync(string? dataUrlInput, CancellationToken cancellationToken)
    {
        // Chỉ nhận PNG/JPEG dạng data URL. Không nhận đường dẫn tùy ý: trường này được nhúng thẳng vào thẻ
        // <img> của trang thư viện, để tự do thì một chuỗi "javascript:..." hay một URL ngoài đều lọt vào.
        var dataUrl = dataUrlInput ?? "";
        var extension = dataUrl.StartsWith("data:image/png;base64,") ? ".png"
            : dataUrl.StartsWith("data:image/jpeg;base64,") ? ".jpg"
            : null;
        if (extension is null) return (null, "Ảnh xem trước không hợp lệ.");

        byte[] imageByteList;
        try
        {
            imageByteList = Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]);
        }
        catch (FormatException)
        {
            return (null, "Ảnh xem trước không hợp lệ.");
        }

        // 2 MB là quá thừa cho một ảnh xem trước vài trăm pixel — vượt mức đó nghĩa là client gửi sai thứ.
        if (imageByteList.Length == 0 || imageByteList.Length > 2 * 1024 * 1024)
            return (null, "Ảnh xem trước không hợp lệ.");

        var folder = Path.Combine(GetWebRoot(), "uploads", LibrarySubFolder, "thumbnail");
        Directory.CreateDirectory(folder);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, storedName), imageByteList, cancellationToken);

        return ($"/uploads/{LibrarySubFolder}/thumbnail/{storedName}", null);
    }

    public async Task<Result> Delete(int id, CancellationToken cancellationToken)
    {
        var asset = await _db.SimAsset.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset is null) return Result.NotFound(new MessageResponse("Không tìm thấy tệp trong thư viện."));

        var usingTitleList = await _db.ScenarioScene.AsNoTracking()
            .Where(x => x.AssetUrl == asset.FileUrl)
            .Select(x => x.Title)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (usingTitleList.Count > 0)
            return Result.Invalid(new MessageResponse(
                $"Tệp này đang được dùng bởi mô phỏng: {string.Join(", ", usingTitleList)}. " +
                "Hãy đổi bối cảnh của các mô phỏng đó trước khi xóa."));

        _db.SimAsset.Remove(asset);
        await _db.SaveChangesAsync(cancellationToken);

        _fileService.Delete(asset.FileUrl);
        if (!string.IsNullOrWhiteSpace(asset.ThumbnailUrl)) _fileService.Delete(asset.ThumbnailUrl);

        _logger.LogInformation("Da xoa tep thu vien 3D #{Id} ({Name}).", asset.Id, asset.Name);
        return Result.Success(new MessageResponse($"Đã xóa \"{asset.Name}\" khỏi thư viện."));
    }

    private static string AssetTypeLabel(string assetType) => assetType switch
    {
        "glb" => "Model 3D",
        "pano-image" => "Ảnh 360°",
        "pano-video" => "Video 360°",
        "image-2d" => "Ảnh thường",
        CharacterAssetType => "Nhân vật",
        _ => assetType
    };

    private string GetWebRoot() => string.IsNullOrEmpty(_env.WebRootPath)
        ? Path.Combine(_env.ContentRootPath, "wwwroot")
        : _env.WebRootPath;
}
