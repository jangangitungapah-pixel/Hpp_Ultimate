using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class AuthService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    private const string EmergencyAdminIdentity = "admin";
    private const string EmergencyAdminPassword = "admin";
    private const string EmergencyAdminEmail = "admin@emergency.local";
    private const string EmergencyAdminName = "Admin Darurat";

    public async Task<AuthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var actor = access.GetActiveUser(clearInvalidSession: true);
        var actorCacheKey = actor is null ? "anon" : $"{actor.Id}:{actor.Role}";
        var cacheKey = $"auth:{store.Version}:{actorCacheKey}";

        if (cache.TryGetValue(cacheKey, out AuthSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(80, cancellationToken);

        snapshot = BuildSnapshot(actor);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Identity) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Task.FromResult(new LoginResult(false, "Email/username dan password wajib diisi."));
        }

        var normalizedIdentity = request.Identity.Trim();
        var normalizedPassword = request.Password.Trim();

        if (normalizedIdentity.Equals(EmergencyAdminIdentity, StringComparison.OrdinalIgnoreCase) &&
            normalizedPassword.Equals(EmergencyAdminPassword, StringComparison.Ordinal))
        {
            var emergencyUser = EnsureEmergencyAdmin();
            return Task.FromResult(CreateLoginSuccessResult(emergencyUser, request.RememberMe, normalizedIdentity, "Login darurat berhasil."));
        }

        var user = store.Users.FirstOrDefault(item =>
            (item.Email.Equals(normalizedIdentity, StringComparison.OrdinalIgnoreCase) ||
             item.Username.Equals(normalizedIdentity, StringComparison.OrdinalIgnoreCase)) &&
            item.Status == UserStatus.Active);

        if (user is null)
        {
            return Task.FromResult(new LoginResult(false, "User tidak ditemukan atau akun nonaktif."));
        }

        var passwordStatus = PasswordHasher.Verify(normalizedPassword, user.Password);
        if (passwordStatus == PasswordVerificationStatus.Failed)
        {
            return Task.FromResult(new LoginResult(false, "Password tidak cocok."));
        }

        var updatedUser = user with
        {
            LastLoginAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            Password = passwordStatus == PasswordVerificationStatus.SuccessRehashNeeded
                ? PasswordHasher.HashPassword(normalizedPassword)
                : user.Password
        };
        store.UpdateUser(updatedUser);
        return Task.FromResult(CreateLoginSuccessResult(updatedUser, request.RememberMe, normalizedIdentity));
    }

    public Task<LoginResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        var actor = access.GetActiveUser(clearInvalidSession: true);
        store.SetSession(null);
        if (actor is not null)
        {
            auditTrail.Record(actor, "Auth", "Logout", "Sesi login", actor.Id, "Sesi login ditutup.");
        }

        return Task.FromResult(new LoginResult(true, "Sesi login ditutup."));
    }

    public Task<AuthMutationResult> SaveCurrentAccountAsync(AccountProfileRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new AuthMutationResult(false, accessDecision.Message));
        }

        var activeUser = accessDecision.Actor!;

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Task.FromResult(new AuthMutationResult(false, "Nama lengkap wajib diisi."));
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Task.FromResult(new AuthMutationResult(false, "Email wajib diisi."));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Task.FromResult(new AuthMutationResult(false, "Username wajib diisi."));
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword) && request.NewPassword.Trim().Length < 6)
        {
            return Task.FromResult(new AuthMutationResult(false, "Password baru minimal 6 karakter."));
        }

        if (store.UserIdentityExists(request.Email.Trim(), request.Username.Trim(), activeUser.Id))
        {
            return Task.FromResult(new AuthMutationResult(false, "Email atau username sudah dipakai user lain."));
        }

        var updated = activeUser with
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Username = request.Username.Trim().ToLowerInvariant(),
            Password = string.IsNullOrWhiteSpace(request.NewPassword)
                ? activeUser.Password
                : PasswordHasher.HashPassword(request.NewPassword.Trim()),
            UpdatedAt = DateTime.Now
        };

        store.UpdateUser(updated);
        store.SetSession(new AuthSession(
            updated.Id,
            updated.FullName,
            updated.Email,
            updated.Role,
            DateTime.Now,
            request.RememberMe));
        auditTrail.Record(updated, "Auth", "Update profil", updated.FullName, updated.Id, "Profil akun aktif diperbarui.");

        return Task.FromResult(new AuthMutationResult(true, "Profil akun berhasil diperbarui.", updated));
    }

    public Task<AuthMutationResult> SaveUserAsync(UserUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            if (accessDecision.Actor is not null)
            {
                auditTrail.Record(accessDecision.Actor, "Security", "Akses ditolak", "Manajemen user", request.Id, accessDecision.Message);
            }

            return Task.FromResult(new AuthMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Task.FromResult(new AuthMutationResult(false, "Nama user wajib diisi."));
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Task.FromResult(new AuthMutationResult(false, "Email wajib diisi."));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Task.FromResult(new AuthMutationResult(false, "Username wajib diisi."));
        }

        var isCreate = request.Id is null;
        var normalizedPassword = request.Password.Trim();
        if (isCreate && string.IsNullOrWhiteSpace(normalizedPassword))
        {
            return Task.FromResult(new AuthMutationResult(false, "Password wajib diisi."));
        }

        if (!string.IsNullOrWhiteSpace(normalizedPassword) && normalizedPassword.Length < 6)
        {
            return Task.FromResult(new AuthMutationResult(false, "Password minimal 6 karakter."));
        }

        if (store.UserIdentityExists(request.Email.Trim(), request.Username.Trim(), request.Id))
        {
            return Task.FromResult(new AuthMutationResult(false, "Email atau username sudah dipakai user lain."));
        }

        if (isCreate)
        {
            var now = DateTime.Now;
            var created = new BusinessUser(
                Guid.NewGuid(),
                request.FullName.Trim(),
                request.Email.Trim().ToLowerInvariant(),
                request.Username.Trim().ToLowerInvariant(),
                request.Role,
                request.Status,
                PasswordHasher.HashPassword(normalizedPassword),
                now,
                now,
                null);

            store.AddUser(created);
            auditTrail.Record(actor, "Auth", "Tambah user", created.FullName, created.Id, $"User {created.FullName} dibuat dengan role {created.Role}.");
            return Task.FromResult(new AuthMutationResult(true, "User berhasil ditambahkan.", created));
        }

        var existing = request.Id is Guid existingId
            ? store.FindUser(existingId)
            : null;
        if (existing is null)
        {
            return Task.FromResult(new AuthMutationResult(false, "User tidak ditemukan."));
        }

        if (!CanModifyAdminState(existing, request.Role, request.Status))
        {
            return Task.FromResult(new AuthMutationResult(false, "Admin aktif terakhir tidak bisa diturunkan role-nya atau dinonaktifkan."));
        }

        var updated = existing with
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Username = request.Username.Trim().ToLowerInvariant(),
            Role = request.Role,
            Status = request.Status,
            Password = string.IsNullOrWhiteSpace(normalizedPassword)
                ? existing.Password
                : PasswordHasher.HashPassword(normalizedPassword),
            UpdatedAt = DateTime.Now
        };

        store.UpdateUser(updated);

        if (store.AuthSession?.UserId == updated.Id)
        {
            if (updated.Status == UserStatus.Active)
            {
                store.SetSession(store.AuthSession with
                {
                    FullName = updated.FullName,
                    Email = updated.Email,
                    Role = updated.Role
                });
            }
            else
            {
                store.SetSession(null);
            }
        }

        auditTrail.Record(actor, "Auth", "Update user", updated.FullName, updated.Id, $"User {updated.FullName} diperbarui ke role {updated.Role} dengan status {updated.Status}.");
        return Task.FromResult(new AuthMutationResult(true, "User berhasil diperbarui.", updated));
    }

    public Task<AuthMutationResult> DeactivateUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            if (accessDecision.Actor is not null)
            {
                auditTrail.Record(accessDecision.Actor, "Security", "Akses ditolak", "Nonaktifkan user", id, accessDecision.Message);
            }

            return Task.FromResult(new AuthMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        var user = store.FindUser(id);
        if (user is null)
        {
            return Task.FromResult(new AuthMutationResult(false, "User tidak ditemukan."));
        }

        if (!CanModifyAdminState(user, user.Role, UserStatus.Inactive))
        {
            return Task.FromResult(new AuthMutationResult(false, "Admin aktif terakhir tidak bisa dinonaktifkan."));
        }

        var updated = user with
        {
            Status = UserStatus.Inactive,
            UpdatedAt = DateTime.Now
        };

        store.UpdateUser(updated);

        if (store.AuthSession?.UserId == id)
        {
            store.SetSession(null);
        }

        auditTrail.Record(actor, "Auth", "Nonaktifkan user", updated.FullName, updated.Id, $"User {updated.FullName} dinonaktifkan.");
        return Task.FromResult(new AuthMutationResult(true, "User dinonaktifkan.", updated));
    }

    private AuthSnapshot BuildSnapshot(BusinessUser? actor)
    {
        var allUsers = store.Users
            .OrderBy(item => item.FullName)
            .Select(item => new UserListItem(item.Id, item.FullName, item.Email, item.Username, item.Role, item.Status, item.UpdatedAt, item.LastLoginAt))
            .ToArray();
        var visibleUsers = actor?.Role == UserRole.Admin
            ? allUsers
            : actor is null
                ? []
                : allUsers.Where(item => item.Id == actor.Id).ToArray();

        var insights = new List<string>();
        if (allUsers.Length == 0)
        {
            insights.Add("Belum ada user. Buat admin pertama dari tombol + User.");
        }

        var inactiveCount = allUsers.Count(item => item.Status == UserStatus.Inactive);
        if (inactiveCount > 0)
        {
            insights.Add($"{inactiveCount} akun saat ini nonaktif.");
        }

        var lastLogin = allUsers.Where(item => item.LastLoginAt.HasValue).OrderByDescending(item => item.LastLoginAt).FirstOrDefault();
        if (lastLogin is not null)
        {
            insights.Add($"Login terbaru dilakukan oleh {lastLogin.FullName} pada {lastLogin.LastLoginAt:dd MMM HH:mm}.");
        }

        var adminCount = allUsers.Count(item => item.Role == UserRole.Admin && item.Status == UserStatus.Active);
        if (adminCount == 0)
        {
            insights.Add("Belum ada admin aktif. Minimal satu admin disarankan.");
        }

        if (insights.Count == 0)
        {
            insights.Add("Struktur akses tim terlihat sehat.");
        }

        return new AuthSnapshot(
            store.AuthSession,
            visibleUsers,
            allUsers.Length,
            allUsers.Count(item => item.Status == UserStatus.Active),
            allUsers.Count(item => item.Role == UserRole.Admin),
            allUsers.Count(item => item.Role == UserRole.Staff),
            insights);
    }

    private bool CanModifyAdminState(BusinessUser existing, UserRole targetRole, UserStatus targetStatus)
    {
        if (existing.Role != UserRole.Admin || existing.Status != UserStatus.Active)
        {
            return true;
        }

        if (targetRole == UserRole.Admin && targetStatus == UserStatus.Active)
        {
            return true;
        }

        return store.Users.Count(item =>
            item.Id != existing.Id &&
            item.Role == UserRole.Admin &&
            item.Status == UserStatus.Active) > 0;
    }

    private LoginResult CreateLoginSuccessResult(
        BusinessUser user,
        bool rememberMe,
        string identity,
        string? successMessage = null)
    {
        var now = DateTime.Now;
        var session = new AuthSession(user.Id, user.FullName, user.Email, user.Role, now, rememberMe);
        store.SetSession(session);
        auditTrail.Record(user, "Auth", "Login", "Sesi login", user.Id, $"Login berhasil dengan identitas {identity}.");
        return new LoginResult(true, successMessage ?? $"Login berhasil sebagai {user.FullName}.", session);
    }

    private BusinessUser EnsureEmergencyAdmin()
    {
        var now = DateTime.Now;
        var existing = store.Users.FirstOrDefault(item =>
            item.Username.Equals(EmergencyAdminIdentity, StringComparison.OrdinalIgnoreCase) ||
            item.Email.Equals(EmergencyAdminEmail, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            var created = new BusinessUser(
                Guid.NewGuid(),
                EmergencyAdminName,
                EmergencyAdminEmail,
                EmergencyAdminIdentity,
                UserRole.Admin,
                UserStatus.Active,
                PasswordHasher.HashPassword(EmergencyAdminPassword),
                now,
                now,
                now);

            return store.AddUser(created);
        }

        var updated = existing with
        {
            FullName = EmergencyAdminName,
            Email = EmergencyAdminEmail,
            Username = EmergencyAdminIdentity,
            Role = UserRole.Admin,
            Status = UserStatus.Active,
            Password = PasswordHasher.HashPassword(EmergencyAdminPassword),
            LastLoginAt = now,
            UpdatedAt = now
        };

        return store.UpdateUser(updated);
    }
}
