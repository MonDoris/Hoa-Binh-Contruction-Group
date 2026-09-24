// File: Application/Features/Ranking/RankingService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Ranking;

public class RankingService : IRankingService
{
    private readonly IAppDbContext _db;
    private readonly IRankingEngine _ranking;
    private readonly ICurrentUser _currentUser;

    public RankingService(IAppDbContext db, IRankingEngine ranking, ICurrentUser currentUser)
    {
        _db = db;
        _ranking = ranking;
        _currentUser = currentUser;
    }

    public async Task<Result> GetMyStatus()
        => Result.Success(await _ranking.GetStatusAsync(_currentUser.UserId));

    public async Task<Result> GetLeaderboard(int top = 50, int? month = null, int? year = null, int? departmentId = null)
    {
        // Trưởng phòng / Giám Đốc chỉ quản lý theo phạm vi phòng ban của mình — khi có yêu cầu lọc theo
        // phòng ban (tab "Top 5 phòng ban"), ép về đúng phòng ban của họ bất kể query string truyền gì,
        // để không xem được xếp hạng nhân viên phòng ban khác. Không ảnh hưởng bảng xếp hạng toàn công ty
        // (không truyền departmentId) — tab đó vẫn công khai cho mọi vai trò như cũ.
        if ((_currentUser.IsHeadOfDepartment || _currentUser.IsDirector) && departmentId.HasValue)
            departmentId = _currentUser.DepartmentId ?? -1;

        return Result.Success(await _ranking.GetLeaderboardAsync(_currentUser.UserId, top, month, year, departmentId));
    }

    public async Task<Result> GetStreakLeaderboard(int top = 20, string? mode = null)
    {
        // Chặn trần để một query string nghịch ngợm (?top=100000) không kéo cả công ty ra dựng chuỗi.
        top = top is < 1 or > 100 ? 20 : top;
        var byLongest = string.Equals(mode, "longest", StringComparison.OrdinalIgnoreCase);
        return Result.Success(await _ranking.GetStreakLeaderboardAsync(_currentUser.UserId, top, byLongest));
    }

    public async Task<Result> GetTierList()
    {
        var myDepartmentRewardMap = _currentUser.DepartmentId.HasValue
            ? await _db.DepartmentRankReward.AsNoTracking()
                .Where(x => x.DepartmentId == _currentUser.DepartmentId)
                .ToDictionaryAsync(x => x.RankTierId, x => x.RewardDescription)
            : new Dictionary<int, string>();

        var list = await _db.RankTier.AsNoTracking()
            .OrderBy(x => x.MinPoints)
            .Select(x => new RankTierDto(
                x.Id, x.Name, x.Icon, x.ColorHex, x.MinPoints, x.RewardDescription, x.OrderIndex,
                x.VoucherCodeList.Count(c => !c.IsUsed), x.VoucherCodeList.Count(c => c.IsUsed), null))
            .ToListAsync();

        var result = list.Select(x => x with
        {
            DepartmentRewardDescription = myDepartmentRewardMap.GetValueOrDefault(x.Id)
        }).ToList();

        return Result.Success(result);
    }

    public async Task<Result> CreateTier(RankTierRequest request)
    {
        _db.RankTier.Add(new RankTier
        {
            Name = request.Name.Trim(),
            Icon = request.Icon.Trim(),
            ColorHex = request.ColorHex.Trim(),
            MinPoints = request.MinPoints,
            RewardDescription = request.RewardDescription?.Trim(),
            OrderIndex = request.OrderIndex,
        });
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã thêm bậc hạng."));
    }

    public async Task<Result> UpdateTier(int id, RankTierRequest request)
    {
        var entity = await _db.RankTier.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy bậc hạng."));

        entity.Name = request.Name.Trim();
        entity.Icon = request.Icon.Trim();
        entity.ColorHex = request.ColorHex.Trim();
        entity.MinPoints = request.MinPoints;
        entity.RewardDescription = request.RewardDescription?.Trim();
        entity.OrderIndex = request.OrderIndex;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã cập nhật bậc hạng."));
    }

    public async Task<Result> DeleteTier(int id)
    {
        var entity = await _db.RankTier.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy bậc hạng."));

        if (await _db.UserRankAward.AnyAsync(x => x.RankTierId == id))
            return Result.Invalid(new MessageResponse("Đã có người học đạt bậc hạng này, không thể xóa."));

        _db.RankTier.Remove(entity);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa bậc hạng."));
    }

    public async Task<Result> SetDepartmentReward(int rankTierId, DepartmentRankRewardRequest request)
    {
        if (!_currentUser.DepartmentId.HasValue) return Result.Invalid(new MessageResponse("Tài khoản chưa gắn phòng ban."));
        if (!await _db.RankTier.AnyAsync(x => x.Id == rankTierId))
            return Result.NotFound(new MessageResponse("Không tìm thấy bậc hạng."));

        var entity = await _db.DepartmentRankReward
            .FirstOrDefaultAsync(x => x.RankTierId == rankTierId && x.DepartmentId == _currentUser.DepartmentId);

        if (entity is null)
        {
            _db.DepartmentRankReward.Add(new DepartmentRankReward
            {
                RankTierId = rankTierId,
                DepartmentId = _currentUser.DepartmentId.Value,
                RewardDescription = request.RewardDescription.Trim(),
            });
        }
        else
        {
            entity.RewardDescription = request.RewardDescription.Trim();
            entity.UpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã lưu phần thưởng của phòng ban."));
    }

    public async Task<Result> DeleteDepartmentReward(int rankTierId)
    {
        if (!_currentUser.DepartmentId.HasValue) return Result.Invalid(new MessageResponse("Tài khoản chưa gắn phòng ban."));

        var entity = await _db.DepartmentRankReward
            .FirstOrDefaultAsync(x => x.RankTierId == rankTierId && x.DepartmentId == _currentUser.DepartmentId);
        if (entity is not null)
        {
            _db.DepartmentRankReward.Remove(entity);
            await _db.SaveChangesAsync();
        }

        return Result.Success(new MessageResponse("Đã xóa phần thưởng của phòng ban."));
    }

    public async Task<Result> GetVoucherCodeList(int id)
    {
        var list = await _db.RankVoucherCode.AsNoTracking()
            .Where(x => x.RankTierId == id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new RankVoucherCodeDto(x.Id, x.Code, x.IsUsed, x.AssignedToUser != null ? x.AssignedToUser.FullName : null, x.AssignedAt))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> AddVoucherCode(int id, AddVoucherCodeRequest request)
    {
        if (!await _db.RankTier.AnyAsync(x => x.Id == id))
            return Result.NotFound(new MessageResponse("Không tìm thấy bậc hạng."));

        var newCodeList = request.Codes
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
        if (newCodeList.Count == 0)
            return Result.Invalid(new MessageResponse("Chưa nhập mã nào hợp lệ."));

        var existingCodeSet = (await _db.RankVoucherCode
            .Where(x => x.RankTierId == id)
            .Select(x => x.Code)
            .ToListAsync()).ToHashSet();

        var addedList = newCodeList.Where(c => !existingCodeSet.Contains(c))
            .Select(c => new RankVoucherCode { RankTierId = id, Code = c })
            .ToList();

        _db.RankVoucherCode.AddRange(addedList);
        await _db.SaveChangesAsync();

        var skippedCount = newCodeList.Count - addedList.Count;
        return Result.Success(new MessageResponse(skippedCount > 0
            ? $"Đã thêm {addedList.Count} mã, bỏ qua {skippedCount} mã trùng."
            : $"Đã thêm {addedList.Count} mã."));
    }

    public async Task<Result> DeleteVoucherCode(int id)
    {
        var entity = await _db.RankVoucherCode.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy mã."));
        if (entity.IsUsed) return Result.Invalid(new MessageResponse("Mã đã được cấp cho người học, không thể xóa."));

        _db.RankVoucherCode.Remove(entity);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa mã."));
    }
}
