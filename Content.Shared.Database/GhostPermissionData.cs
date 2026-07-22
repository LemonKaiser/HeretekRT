using System;

namespace Content.Shared.Database;

/// <summary>
/// Authoritative data for an account's limited permission to become an observer.
/// This type is persisted only on the server and must never be sent to clients.
/// </summary>
public sealed record GhostPermissionData(int RemainingUses, DateTime? ExpiresAt);
