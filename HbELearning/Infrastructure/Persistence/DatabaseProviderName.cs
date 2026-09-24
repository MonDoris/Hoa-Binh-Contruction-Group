// File: Infrastructure/Persistence/DatabaseProviderName.cs

namespace HbELearning.Infrastructure.Persistence;

/// <summary>
/// Tên provider database đã chọn ("Sqlite" / "SqlServer" / "Oracle"), đặt vào DI lúc lắp ráp.
///
/// Trước khi tách lớp, Program.cs giữ giá trị này trong một biến cục bộ rồi dùng lại ở đoạn khởi tạo
/// database bên dưới. Giờ hai việc đó nằm ở hai project khác nhau, nên giá trị đi qua DI thay vì qua
/// biến — và nhờ vậy Program.cs không cần biết tới DbProviderSetup nữa.
/// </summary>
public sealed record DatabaseProviderName(string Value);
