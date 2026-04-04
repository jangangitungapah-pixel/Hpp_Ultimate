using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public enum UserRole
{
    Admin,
    Staff
}

public enum UserStatus
{
    Active,
    Inactive
}

public sealed record BusinessUser(
    Guid Id,
    string FullName,
    string Email,
    string Username,
    UserRole Role,
    UserStatus Status,
    string Password,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastLoginAt);

public sealed record AuthSession(
    Guid UserId,
    string FullName,
    string Email,
    UserRole Role,
    DateTime SignedInAt,
    bool RememberMe);

public sealed record UserListItem(
    Guid Id,
    string FullName,
    string Email,
    string Username,
    UserRole Role,
    UserStatus Status,
    DateTime UpdatedAt,
    DateTime? LastLoginAt);

public sealed record AuthSnapshot(
    AuthSession? Session,
    IReadOnlyList<UserListItem> Users,
    int TotalUsers,
    int ActiveUsers,
    int AdminCount,
    int StaffCount,
    IReadOnlyList<string> Insights);

public sealed record AuthMutationResult(bool Success, string Message, BusinessUser? User = null);

public sealed record LoginResult(bool Success, string Message, AuthSession? Session = null);

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Email atau username wajib diisi.")]
    public string Identity { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed class UserUpsertRequest
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Nama user wajib diisi.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username wajib diisi.")]
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Staff;
    public UserStatus Status { get; set; } = UserStatus.Active;
}

public sealed class AccountProfileRequest
{
    [Required(ErrorMessage = "Nama lengkap wajib diisi.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username wajib diisi.")]
    public string Username { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}
