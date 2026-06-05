namespace AudioYotoShelf.Core.Entities;

/// <summary>
/// One row per successful ABS login (a session start). The session itself is a stateless cookie,
/// so this audit table is what powers login/session counts and active-user metrics over time.
/// <see cref="BaseEntity.CreatedAt"/> is the moment of login.
/// </summary>
public class LoginEvent : BaseEntity
{
    public required Guid UserConnectionId { get; set; }
    public UserConnection? UserConnection { get; set; }
}
