using AudioYotoShelf.Api.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AudioYotoShelf.Api.Tests;

public class EnvironmentAliasesTests
{
    private static EnvironmentAliases.Resolution Resolve(
        Dictionary<string, string?> environment, Dictionary<string, string>? files = null) =>
        EnvironmentAliases.Resolve(environment, path =>
            files is not null && files.TryGetValue(path, out var contents)
                ? contents
                : throw new FileNotFoundException("missing", path));

    // ---- one-to-one aliases ----

    [Theory]
    [InlineData("YOTO_CLIENT_ID", "Yoto:ClientId")]
    [InlineData("YOTO_CLIENT_SECRET", "Yoto:ClientSecret")]
    [InlineData("GEMINI_API_KEY", "Gemini:ApiKey")]
    [InlineData("GEMINI_MODEL", "Gemini:Model")]
    [InlineData("AUDIOBOOKSHELF_URL", "Audiobookshelf:Url")]
    [InlineData("AUDIOBOOKSHELF_PUBLIC_URL", "Audiobookshelf:PublicUrl")]
    [InlineData("ADMIN_AUDIOBOOKSHELF_URL", "Admin:AudiobookshelfUrl")]
    [InlineData("ADMIN_USERNAMES", "Admin:Usernames")]
    [InlineData("DB_CONNECTION_STRING", "ConnectionStrings:Postgres")]
    [InlineData("REDIS_CONNECTION_STRING", "ConnectionStrings:Redis")]
    [InlineData("LOG_LEVEL", "Serilog:MinimumLevel:Default")]
    [InlineData("SEQ_URL", "Serilog:SeqUrl")]
    [InlineData("TRANSFER_TEMP_DIR", "Transfer:TempDirectory")]
    [InlineData("HANGFIRE_DASHBOARD_ENABLED", "Hangfire:DashboardEnabled")]
    public void Alias_MapsOntoItsConfigurationKey(string variable, string key)
    {
        var result = Resolve(new() { [variable] = "value" });

        result.Values.Should().ContainKey(key).WhoseValue.Should().Be("value");
        result.Applied.Should().Equal(variable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankAlias_IsTreatedAsUnset(string blank)
    {
        // `GEMINI_MODEL=` in an .env file must not replace the default model with "".
        var result = Resolve(new() { ["GEMINI_MODEL"] = blank });

        result.Values.Should().BeEmpty();
        result.Applied.Should().BeEmpty();
    }

    [Fact]
    public void UnrelatedVariables_AreIgnored()
    {
        Resolve(new() { ["PATH"] = "/usr/bin", ["HOME"] = "/root" }).Values.Should().BeEmpty();
    }

    // ---- _FILE secrets ----

    [Fact]
    public void FileVariant_ReadsTheSecretAndTrimsTrailingNewlines()
    {
        var result = Resolve(
            new() { ["YOTO_CLIENT_SECRET_FILE"] = "/run/secrets/yoto" },
            new() { ["/run/secrets/yoto"] = "s3cret\r\n" });

        result.Values["Yoto:ClientSecret"].Should().Be("s3cret");
        result.Applied.Should().Equal("YOTO_CLIENT_SECRET_FILE");
    }

    [Fact]
    public void FileVariant_BothSet_FailsNamingTheVariable()
    {
        var act = () => Resolve(new() { ["GEMINI_API_KEY"] = "a", ["GEMINI_API_KEY_FILE"] = "/run/secrets/gemini" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*GEMINI_API_KEY*GEMINI_API_KEY_FILE*");
    }

    [Fact]
    public void FileVariant_MissingFile_FailsNamingThePath()
    {
        var act = () => Resolve(new() { ["GEMINI_API_KEY_FILE"] = "/run/secrets/nope" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*GEMINI_API_KEY_FILE*/run/secrets/nope*");
    }

    [Fact]
    public void FileVariant_OnlyForSecrets()
    {
        // A non-secret has no _FILE form, so the variable is simply ignored.
        Resolve(new() { ["YOTO_CLIENT_ID_FILE"] = "/run/secrets/id" }).Values.Should().BeEmpty();
    }

    // ---- Postgres composed from DB_* ----

    [Fact]
    public void DbPasswordAlone_ComposesWithComposeDefaults()
    {
        var result = Resolve(new() { ["DB_PASSWORD"] = "pw" });

        var built = new NpgsqlConnectionStringBuilder(result.Values["ConnectionStrings:Postgres"]);
        built.Host.Should().Be("postgres");
        built.Port.Should().Be(5432);
        built.Database.Should().Be("audioyotoshelf");
        built.Username.Should().Be("bridge");
        built.Password.Should().Be("pw");
        result.Applied.Should().Equal("DB_PASSWORD");
    }

    [Fact]
    public void DbParts_AreAllHonoured()
    {
        var result = Resolve(new()
        {
            ["DB_HOST"] = "db.internal",
            ["DB_PORT"] = "6543",
            ["DB_NAME"] = "ays",
            ["DB_USER"] = "svc",
            ["DB_PASSWORD"] = "pw",
        });

        var built = new NpgsqlConnectionStringBuilder(result.Values["ConnectionStrings:Postgres"]);
        (built.Host, built.Port, built.Database, built.Username).Should().Be(("db.internal", 6543, "ays", "svc"));
    }

    [Fact]
    public void DbPassword_WithConnectionStringSyntax_SurvivesIntact()
    {
        const string password = "p;w=d'\"x";

        var result = Resolve(new() { ["DB_PASSWORD"] = password });

        new NpgsqlConnectionStringBuilder(result.Values["ConnectionStrings:Postgres"]).Password.Should().Be(password);
    }

    [Fact]
    public void DbPassword_FromFile()
    {
        var result = Resolve(
            new() { ["DB_PASSWORD_FILE"] = "/run/secrets/db" },
            new() { ["/run/secrets/db"] = "filepw\n" });

        new NpgsqlConnectionStringBuilder(result.Values["ConnectionStrings:Postgres"]).Password.Should().Be("filepw");
    }

    [Fact]
    public void DbParts_WithoutPassword_Fail()
    {
        var act = () => Resolve(new() { ["DB_HOST"] = "db.internal" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*DB_PASSWORD*");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public void DbPort_Invalid_Fails(string port)
    {
        var act = () => Resolve(new() { ["DB_PORT"] = port, ["DB_PASSWORD"] = "pw" });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*DB_PORT*{port}*");
    }

    [Fact]
    public void DbConnectionString_WinsOverParts()
    {
        var result = Resolve(new() { ["DB_CONNECTION_STRING"] = "Host=elsewhere", ["DB_HOST"] = "ignored" });

        result.Values["ConnectionStrings:Postgres"].Should().Be("Host=elsewhere");
        result.Applied.Should().Equal("DB_CONNECTION_STRING");
    }

    [Fact]
    public void NoDbVariables_LeavesPostgresToAppSettings()
    {
        Resolve([]).Values.Should().NotContainKey("ConnectionStrings:Postgres");
    }

    // ---- Redis default inside a container ----

    [Fact]
    public void InContainer_RedisDefaultsToTheComposeService()
    {
        var result = Resolve(new() { ["DOTNET_RUNNING_IN_CONTAINER"] = "true" });

        result.Values["ConnectionStrings:Redis"].Should().Be("redis:6379");
        result.Applied.Should().BeEmpty();
    }

    [Fact]
    public void InContainer_ExplicitRedisWins()
    {
        var result = Resolve(new() { ["DOTNET_RUNNING_IN_CONTAINER"] = "true", ["REDIS_CONNECTION_STRING"] = "cache:6380" });

        result.Values["ConnectionStrings:Redis"].Should().Be("cache:6380");
    }

    [Fact]
    public void OutsideContainer_NoRedisDefault()
    {
        Resolve([]).Values.Should().NotContainKey("ConnectionStrings:Redis");
    }

    // ---- registration order ----

    [Fact]
    public void AddEnvironmentAliases_SitsBelowNativeEnvironmentVariables()
    {
        const string alias = "YOTO_CLIENT_ID";
        const string native = "Yoto__ClientId";
        var previousAlias = Environment.GetEnvironmentVariable(alias);
        var previousNative = Environment.GetEnvironmentVariable(native);
        try
        {
            Environment.SetEnvironmentVariable(alias, "from-alias");
            Environment.SetEnvironmentVariable(native, "from-native");

            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Yoto:ClientId"] = "from-appsettings" })
                .AddEnvironmentVariables();
            builder.AddEnvironmentAliases();
            builder.Build()["Yoto:ClientId"].Should().Be("from-native");

            Environment.SetEnvironmentVariable(native, null);
            var withoutNative = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Yoto:ClientId"] = "from-appsettings" })
                .AddEnvironmentVariables();
            withoutNative.AddEnvironmentAliases();
            withoutNative.Build()["Yoto:ClientId"].Should().Be("from-alias");
        }
        finally
        {
            Environment.SetEnvironmentVariable(alias, previousAlias);
            Environment.SetEnvironmentVariable(native, previousNative);
        }
    }
}
