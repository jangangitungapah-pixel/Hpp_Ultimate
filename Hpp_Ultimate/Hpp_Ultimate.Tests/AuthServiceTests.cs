using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;
using Hpp_Ultimate.Services;
using Xunit;

namespace Hpp_Ultimate.Tests;

public sealed class AuthServiceTests
{
    private static AuthService CreateService(IMemoryCache cache, SeededBusinessDataStore store)
        => new(cache, store, new WorkspaceAccessService(store), new AuditTrailService(store));

    [Fact]
    public void PasswordHasher_RoundTrip_Succeeds()
    {
        var hash = PasswordHasher.HashPassword("secret123");

        var result = PasswordHasher.Verify("secret123", hash);

        Assert.Equal(PasswordVerificationStatus.Success, result);
    }

    [Fact]
    public async Task LoginAsync_LegacyPassword_UpgradesStoredHash()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        var user = new BusinessUser(Guid.NewGuid(), "Admin", "admin@test.local", "admin", UserRole.Admin, UserStatus.Active, "secret123", now, now, null);
        store.AddUser(user);
        var service = CreateService(cache, store);

        var result = await service.LoginAsync(new LoginRequest
        {
            Identity = "admin",
            Password = "secret123"
        });

        var persisted = store.FindUser(user.Id);
        Assert.True(result.Success);
        Assert.NotNull(persisted);
        Assert.NotEqual("secret123", persisted!.Password);
        Assert.Equal(PasswordVerificationStatus.Success, PasswordHasher.Verify("secret123", persisted.Password));
    }

    [Fact]
    public async Task SaveUserAsync_RequiresAdminSession()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        var staff = new BusinessUser(Guid.NewGuid(), "Staff", "staff@test.local", "staff", UserRole.Staff, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        store.AddUser(staff);
        store.SetSession(new AuthSession(staff.Id, staff.FullName, staff.Email, staff.Role, now, false));
        var service = CreateService(cache, store);

        var result = await service.SaveUserAsync(new UserUpsertRequest
        {
            FullName = "New User",
            Email = "new@test.local",
            Username = "newuser",
            Password = "secret123",
            Role = UserRole.Staff,
            Status = UserStatus.Active
        });

        Assert.False(result.Success);
        Assert.Equal("Aksi ini hanya tersedia untuk admin.", result.Message);
    }

    [Fact]
    public async Task SaveUserAsync_BlankPasswordOnEdit_KeepsExistingPassword()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        var admin = new BusinessUser(Guid.NewGuid(), "Admin", "admin@test.local", "admin", UserRole.Admin, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        var staff = new BusinessUser(Guid.NewGuid(), "Staff", "staff@test.local", "staff", UserRole.Staff, UserStatus.Active, PasswordHasher.HashPassword("secret456"), now, now, null);
        store.AddUser(admin);
        store.AddUser(staff);
        store.SetSession(new AuthSession(admin.Id, admin.FullName, admin.Email, admin.Role, now, false));
        var service = CreateService(cache, store);

        var result = await service.SaveUserAsync(new UserUpsertRequest
        {
            Id = staff.Id,
            FullName = "Staff Updated",
            Email = "staff@test.local",
            Username = "staff",
            Password = string.Empty,
            Role = UserRole.Staff,
            Status = UserStatus.Active
        });

        var updated = store.FindUser(staff.Id);
        Assert.True(result.Success);
        Assert.NotNull(updated);
        Assert.Equal(staff.Password, updated!.Password);
    }

    [Fact]
    public async Task DeactivateUserAsync_BlocksLastActiveAdmin()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        var admin = new BusinessUser(Guid.NewGuid(), "Admin", "admin@test.local", "admin", UserRole.Admin, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        store.AddUser(admin);
        store.SetSession(new AuthSession(admin.Id, admin.FullName, admin.Email, admin.Role, now, false));
        var service = CreateService(cache, store);

        var result = await service.DeactivateUserAsync(admin.Id);

        Assert.False(result.Success);
        Assert.Equal("Admin aktif terakhir tidak bisa dinonaktifkan.", result.Message);
    }
}
