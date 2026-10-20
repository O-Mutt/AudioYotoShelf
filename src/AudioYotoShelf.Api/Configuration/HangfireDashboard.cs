namespace AudioYotoShelf.Api.Configuration;

/// <summary>The opt-in Hangfire dashboard. Off unless <see cref="EnabledConfigKey"/> is true.</summary>
public static class HangfireDashboard
{
    public const string EnabledConfigKey = "Hangfire:DashboardEnabled";
    public const string Path = "/hangfire";
}
