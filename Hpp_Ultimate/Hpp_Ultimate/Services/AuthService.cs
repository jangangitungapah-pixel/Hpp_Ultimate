using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class AuthService(IMemoryCache cache, SeededBusinessDataStore store)
{
    public async Task<AuthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"auth:{store.Version}";

        if (cache.TryGetValue(cacheKey, out AuthSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(80, cancellationToken);

        snapshot = BuildSnapshot();
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Identity) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Task.FromResult(new LoginResult(false, "Email/username dan password wajib diisi."));
        }

        var user = store.Users.FirstOrDefault(item =>
            (item.Email.Equals(request.Identity.Trim(), StringComparison.OrdinalIgnoreCase) ||
             item.Username.Equals(request.Identity.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            item.Status == UserStatus.Active);

        if (user is null)
        {
            return Task.FromResult(new LoginResult(false, "User tidak ditemukan atau akun nonaktif."));
        }

        if (!user.Password.Equals(request.Password, StringComparison.Ordinal))
        {
            return Task.FromResult(new LoginResult(false, "Password tidak cocok."));
        }

        var now = DateTime.Now;
        var updatedUser = user with { LastLoginAt = now, UpdatedAt = now };
        store.UpdateUser(updatedUser);

        var session = new AuthSession(updatedUser.Id, updatedUser.FullName, updatedUser.Email, updatedUser.Role, now, request.RememberMe);
        store.SetSession(session);
        return Task.FromResult(new LoginResult(true, $"Login berhasil sebagai {updatedUser.FullName}.", session));
    }

    public Task<LoginResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        store.SetSession(null);
        return Task.FromResult(new LoginResult(true, "Sesi login ditutup."));
    }

    public Task<AuthMutationResult> SaveCurrentAccountAsync(AccountProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (store.AuthSession is null)
        {
            return Task.FromResult(new AuthMutationResult(false, "Sesi login tidak ditemukan. Silakan masuk ulang."));
        }

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

        var existing = store.FindUser(store.AuthSession.UserId);
        if (existing is null)
        {
            store.SetSession(null);
            return Task.FromResult(new AuthMutationResult(false, "Akun aktif tidak ditemukan. Sesi login ditutup."));
        }

        if (store.UserIdentityExists(request.Email.Trim(), request.Username.Trim(), existing.Id))
        {
            return Task.FromResult(new AuthMutationResult(false, "Email atau username sudah dipakai user lain."));
        }

        var updated = existing with
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Username = request.Username.Trim().ToLowerInvariant(),
            Password = string.IsNullOrWhiteSpace(request.NewPassword) ? existing.Password : request.NewPassword,
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

        return Task.FromResult(new AuthMutationResult(true, "Profil akun berhasil diperbarui.", updated));
    }

    public Task<AuthMutationResult> SaveUserAsync(UserUpsertRequest request, CancellationToken cancellationToken = default)
    {
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

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Task.FromResult(new AuthMutationResult(false, "Password wajib diisi."));
        }

        if (store.UserIdentityExists(request.Email.Trim(), request.Username.Trim(), request.Id))
        {
            return Task.FromResult(new AuthMutationResult(false, "Email atau username sudah dipakai user lain."));
        }

        if (request.Id is null)
        {
            var now = DateTime.Now;
            var created = new BusinessUser(
                Guid.NewGuid(),
                request.FullName.Trim(),
                request.Email.Trim().ToLowerInvariant(),
                request.Username.Trim().ToLowerInvariant(),
                request.Role,
                request.Status,
                request.Password,
                now,
                now,
                null);

            store.AddUser(created);
            return Task.FromResult(new AuthMutationResult(true, "User berhasil ditambahkan.", created));
        }

        var existing = store.FindUser(request.Id.Value);
        if (existing is null)
        {
            return Task.FromResult(new AuthMutationResult(false, "User tidak ditemukan."));
        }

        var updated = existing with
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Username = request.Username.Trim().ToLowerInvariant(),
            Role = request.Role,
            Status = request.Status,
            Password = request.Password,
            UpdatedAt = DateTime.Now
        };

        store.UpdateUser(updated);

        if (store.AuthSession?.UserId == updated.Id)
        {
            store.SetSession(store.AuthSession with
            {
                FullName = updated.FullName,
                Email = updated.Email,
                Role = updated.Role
            });
        }

        return Task.FromResult(new AuthMutationResult(true, "User berhasil diperbarui.", updated));
    }

    public Task<AuthMutationResult> DeactivateUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = store.FindUser(id);
        if (user is null)
        {
            return Task.FromResult(new AuthMutationResult(false, "User tidak ditemukan."));
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

        return Task.FromResult(new AuthMutationResult(true, "User dinonaktifkan.", updated));
    }

    private AuthSnapshot BuildSnapshot()
    {
        var users = store.Users
            .OrderBy(item => item.FullName)
            .Select(item => new UserListItem(item.Id, item.FullName, item.Email, item.Username, item.Role, item.Status, item.UpdatedAt, item.LastLoginAt))
            .ToArray();

        var insights = new List<string>();
        if (users.Length == 0)
        {
            insights.Add("Belum ada user. Buat admin pertama dari tombol + User.");
        }

        var inactiveCount = users.Count(item => item.Status == UserStatus.Inactive);
        if (inactiveCount > 0)
        {
            insights.Add($"{inactiveCount} akun saat ini nonaktif.");
        }

        var lastLogin = users.Where(item => item.LastLoginAt.HasValue).OrderByDescending(item => item.LastLoginAt).FirstOrDefault();
        if (lastLogin is not null)
        {
            insights.Add($"Login terbaru dilakukan oleh {lastLogin.FullName} pada {lastLogin.LastLoginAt:dd MMM HH:mm}.");
        }

        var adminCount = users.Count(item => item.Role == UserRole.Admin && item.Status == UserStatus.Active);
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
            users,
            users.Length,
            users.Count(item => item.Status == UserStatus.Active),
            users.Count(item => item.Role == UserRole.Admin),
            users.Count(item => item.Role == UserRole.Staff),
            insights);
    }
}
