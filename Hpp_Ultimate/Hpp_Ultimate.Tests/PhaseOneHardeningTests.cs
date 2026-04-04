using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;
using Hpp_Ultimate.Services;
using Xunit;

namespace Hpp_Ultimate.Tests;

public sealed class PhaseOneHardeningTests
{
    [Fact]
    public async Task SettingsService_SaveAsync_RequiresAdminSession()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        var staff = new BusinessUser(Guid.NewGuid(), "Staff", "staff@test.local", "staff", UserRole.Staff, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        store.AddUser(staff);
        store.SetSession(new AuthSession(staff.Id, staff.FullName, staff.Email, staff.Role, now, false));

        var service = new SettingsService(cache, store, new WorkspaceAccessService(store), new AuditTrailService(store));
        var result = await service.SaveAsync(new BusinessSettingsRequest
        {
            BusinessName = "Usaha Staff"
        });

        Assert.False(result.Success);
        Assert.Equal("Aksi ini hanya tersedia untuk admin.", result.Message);
    }

    [Fact]
    public async Task SettingsService_GetSnapshotAsync_ForStaff_IsReadOnlyAndHidesAuditTrail()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        var staff = new BusinessUser(Guid.NewGuid(), "Staff", "staff@test.local", "staff", UserRole.Staff, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        store.AddUser(staff);
        store.SetSession(new AuthSession(staff.Id, staff.FullName, staff.Email, staff.Role, now, false));
        store.AddAuditLog(new AuditLogEntry(Guid.NewGuid(), "Settings", "Simpan pengaturan", "Admin", "admin@test.local", Guid.NewGuid(), "Usaha", null, "Pengaturan diubah.", now));

        var service = new SettingsService(cache, store, new WorkspaceAccessService(store), new AuditTrailService(store));
        var snapshot = await service.GetSnapshotAsync();

        Assert.False(snapshot.CanManageSettings);
        Assert.False(snapshot.CanViewAuditTrail);
        Assert.Empty(snapshot.RecentAuditEntries);
    }

    [Fact]
    public async Task RawMaterialCatalogService_CreateAsync_RequiresActiveSession()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new RawMaterialCatalogService(cache, store, new WorkspaceAccessService(store), new AuditTrailService(store));

        var result = await service.CreateAsync(new RawMaterialUpsertRequest
        {
            Code = "BHN-001",
            Name = "Gula",
            BaseUnit = "gr",
            NetQuantity = 1000,
            NetUnit = "gr",
            PricePerPack = 16000,
            Status = MaterialStatus.Active
        });

        Assert.False(result.Success);
        Assert.Equal("Sesi login tidak ditemukan. Silakan masuk ulang.", result.Message);
    }

    [Fact]
    public async Task RawMaterialCatalogService_CreateAsync_WritesAuditTrail()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        var staff = new BusinessUser(Guid.NewGuid(), "Staff", "staff@test.local", "staff", UserRole.Staff, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        store.AddUser(staff);
        store.SetSession(new AuthSession(staff.Id, staff.FullName, staff.Email, staff.Role, now, false));
        var service = new RawMaterialCatalogService(cache, store, new WorkspaceAccessService(store), new AuditTrailService(store));

        var result = await service.CreateAsync(new RawMaterialUpsertRequest
        {
            Code = "BHN-001",
            Name = "Gula",
            BaseUnit = "gr",
            NetQuantity = 1000,
            NetUnit = "gr",
            PricePerPack = 16000,
            Status = MaterialStatus.Active
        });

        Assert.True(result.Success);
        Assert.Contains(store.AuditLogEntries, item => item.Area == "Material" && item.Action == "Tambah material" && item.Subject == "Gula");
    }
}
