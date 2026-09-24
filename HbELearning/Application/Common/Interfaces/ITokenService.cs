// File: Application/Common/Interfaces/ITokenService.cs
// Cong (port) tach ra tu Infrastructure/Identity/TokenService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user);
}
