using Microsoft.Extensions.Logging.Abstractions;
using Hpp_Ultimate.Services;

namespace Hpp_Ultimate.Tests;

internal sealed class TestStoreScope : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hpp-ultimate-tests-{Guid.NewGuid():N}.db");

    public TestStoreScope()
    {
        var options = SeededBusinessDataStoreOptions.Create(_dbPath, postgresConnectionString: null);
        Store = new SeededBusinessDataStore(options, NullLogger<SeededBusinessDataStore>.Instance);
        Store.ClearOperationalData();
        Store.SetSession(null);
    }

    public SeededBusinessDataStore Store { get; }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
        }
    }
}
