namespace AudioYotoShelf.Core.DTOs.Admin;

/// <summary>Headline usage numbers for the admin dashboard.</summary>
public record AdminOverview(
    int TotalUsers,
    int AbsConnectedUsers,
    int YotoConnectedUsers,
    int AdminUsers,
    int ActiveUsers7d,
    int ActiveUsers30d,
    int TotalLogins,
    int Logins7d,
    int Logins30d,
    int TotalTransfers,
    int CompletedTransfers,
    int FailedTransfers,
    double TransferSuccessRate,
    int Transfers7d,
    int TotalPlaylists);

/// <summary>One row per user in the admin user table.</summary>
public record AdminUserRow(
    Guid Id,
    string Username,
    bool IsAdmin,
    bool AbsConnected,
    bool YotoConnected,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    int LoginCount,
    int TransferCount);

/// <summary>A single day in the usage time series.</summary>
public record UsagePoint(
    DateOnly Date,
    int Logins,
    int Transfers);
