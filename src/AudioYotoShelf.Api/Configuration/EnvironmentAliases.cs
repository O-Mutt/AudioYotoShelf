using System.Collections;
using AudioYotoShelf.Core.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Npgsql;

namespace AudioYotoShelf.Api.Configuration;

/// <summary>
/// Flat environment variables for container deployments (<c>YOTO_CLIENT_ID</c>, <c>DB_PASSWORD</c>, …),
/// mapped onto the app's configuration keys so the published image runs from <c>docker run -e</c>
/// without knowing ASP.NET's <c>Section__Key</c> naming. Registered just below the native environment
/// source, so a native variable such as <c>Yoto__ClientId</c> still wins over its alias.
/// </summary>
public static class EnvironmentAliases
{
    internal const string PostgresKey = "ConnectionStrings:Postgres";
    internal const string RedisKey = "ConnectionStrings:Redis";

    /// <summary>The Redis address used inside a container when none is given: the Compose service name.</summary>
    internal const string ContainerRedisDefault = "redis:6379";

    internal static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["YOTO_CLIENT_ID"] = "Yoto:ClientId",
        ["YOTO_CLIENT_SECRET"] = "Yoto:ClientSecret",
        ["GEMINI_API_KEY"] = "Gemini:ApiKey",
        ["GEMINI_MODEL"] = "Gemini:Model",
        ["AUDIOBOOKSHELF_URL"] = AudiobookshelfServer.UrlConfigKey,
        ["AUDIOBOOKSHELF_PUBLIC_URL"] = AudiobookshelfServer.PublicUrlConfigKey,
        ["ADMIN_AUDIOBOOKSHELF_URL"] = "Admin:AudiobookshelfUrl",
        ["ADMIN_USERNAMES"] = "Admin:Usernames",
        ["DB_CONNECTION_STRING"] = PostgresKey,
        ["REDIS_CONNECTION_STRING"] = RedisKey,
        ["LOG_LEVEL"] = "Serilog:MinimumLevel:Default",
        ["SEQ_URL"] = "Serilog:SeqUrl",
        ["TRANSFER_TEMP_DIR"] = "Transfer:TempDirectory",
        ["HANGFIRE_DASHBOARD_ENABLED"] = "Hangfire:DashboardEnabled",
    };

    /// <summary>Secrets that may instead be read from the file named by <c>NAME_FILE</c> (Docker/Kubernetes secrets).</summary>
    internal static readonly IReadOnlySet<string> FileBacked = new HashSet<string>
    {
        "YOTO_CLIENT_SECRET", "GEMINI_API_KEY", "DB_PASSWORD", "DB_CONNECTION_STRING", "REDIS_CONNECTION_STRING",
    };

    /// <summary>Parts the Postgres connection string is composed from when <c>DB_CONNECTION_STRING</c> is not set.</summary>
    private static readonly string[] DbParts = ["DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD"];

    internal sealed record Resolution(IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Applied);

    /// <summary>
    /// Adds the aliases from the process environment. Returns the names of the variables that were
    /// applied (never their values) so startup can log them.
    /// </summary>
    /// <exception cref="InvalidOperationException">A variable is set both directly and via <c>_FILE</c>,
    /// a secret file cannot be read, or the database settings are incomplete or malformed.</exception>
    public static IReadOnlyList<string> AddEnvironmentAliases(this IConfigurationBuilder builder)
    {
        var environment = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value);
        var resolution = Resolve(environment, File.ReadAllText);

        // Below the unprefixed environment source (native Section__Key variables), above appsettings.
        var nativeEnvironment = builder.Sources
            .LastOrDefault(s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
        var index = nativeEnvironment is null ? builder.Sources.Count : builder.Sources.IndexOf(nativeEnvironment);
        builder.Sources.Insert(index, new MemoryConfigurationSource { InitialData = resolution.Values });

        return resolution.Applied;
    }

    internal static Resolution Resolve(IReadOnlyDictionary<string, string?> environment, Func<string, string> readFile)
    {
        var values = new Dictionary<string, string?>();
        var applied = new List<string>();

        string? Read(string name)
        {
            var direct = Blank(environment.GetValueOrDefault(name));
            var file = FileBacked.Contains(name) ? Blank(environment.GetValueOrDefault(name + "_FILE")) : null;

            if (direct is not null && file is not null)
                throw new InvalidOperationException($"Set either {name} or {name}_FILE, not both.");

            if (file is not null)
            {
                string contents;
                try
                {
                    contents = readFile(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException($"{name}_FILE names \"{file}\", which could not be read: {ex.Message}", ex);
                }

                applied.Add(name + "_FILE");
                return contents.TrimEnd('\r', '\n');
            }

            if (direct is not null)
                applied.Add(name);
            return direct;
        }

        foreach (var (name, key) in Aliases)
        {
            if (Read(name) is { } value)
                values[key] = value;
        }

        if (!values.ContainsKey(PostgresKey) && ComposePostgres(Read) is { } postgres)
            values[PostgresKey] = postgres;

        // Inside a container "localhost" is the container itself, so appsettings' default is never right.
        if (!values.ContainsKey(RedisKey) && environment.GetValueOrDefault("DOTNET_RUNNING_IN_CONTAINER") == "true")
            values[RedisKey] = ContainerRedisDefault;

        return new Resolution(values, applied);
    }

    private static string? ComposePostgres(Func<string, string?> read)
    {
        var parts = DbParts.ToDictionary(p => p, read);
        if (parts.Values.All(v => v is null))
            return null;

        var password = parts["DB_PASSWORD"]
            ?? throw new InvalidOperationException(
                "DB_PASSWORD (or DB_PASSWORD_FILE) is required when the database is configured with DB_* variables.");

        var port = 5432;
        if (parts["DB_PORT"] is { } portText && (!int.TryParse(portText, out port) || port is < 1 or > 65535))
            throw new InvalidOperationException($"DB_PORT must be a port number, but was \"{portText}\".");

        // The builder quotes values, so a password containing ';' or '=' survives intact.
        return new NpgsqlConnectionStringBuilder
        {
            Host = parts["DB_HOST"] ?? "postgres",
            Port = port,
            Database = parts["DB_NAME"] ?? "audioyotoshelf",
            Username = parts["DB_USER"] ?? "bridge",
            Password = password,
        }.ConnectionString;
    }

    // Compose passes `NAME=` from an .env line as an empty string; treat it as unset rather than
    // overriding a default with "".
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
