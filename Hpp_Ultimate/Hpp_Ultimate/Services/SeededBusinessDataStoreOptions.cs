using Npgsql;

namespace Hpp_Ultimate.Services;

public enum BusinessDataStoreProvider
{
    Sqlite,
    Postgres
}

public sealed record SeededBusinessDataStoreOptions(
    BusinessDataStoreProvider Provider,
    string ConnectionString,
    string? LocalSqlitePath)
{
    public static SeededBusinessDataStoreOptions Create(string sqlitePath, string? postgresConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return new SeededBusinessDataStoreOptions(
                BusinessDataStoreProvider.Postgres,
                NormalizePostgresConnectionString(postgresConnectionString),
                sqlitePath);
        }

        return new SeededBusinessDataStoreOptions(
            BusinessDataStoreProvider.Sqlite,
            $"Data Source={sqlitePath}",
            sqlitePath);
    }

    private static string NormalizePostgresConnectionString(string rawConnectionString)
    {
        var trimmed = rawConnectionString.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
        var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = SslMode.Require
        };

        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            var query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var pair in query)
            {
                var parts = pair.Split('=', 2, StringSplitOptions.None);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

                switch (key.ToLowerInvariant())
                {
                    case "sslmode":
                        if (Enum.TryParse<SslMode>(value, true, out var sslMode))
                        {
                            builder.SslMode = sslMode;
                        }
                        break;
                    case "options":
                        builder.Options = value;
                        break;
                    case "dbname":
                    case "database":
                        builder.Database = value;
                        break;
                }
            }
        }

        return builder.ConnectionString;
    }
}
