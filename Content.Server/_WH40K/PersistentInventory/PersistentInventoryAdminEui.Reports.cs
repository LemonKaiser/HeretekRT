using System.Linq;
using System.Text;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server.Database;
using Content.Shared._WH40K.PersistentInventory;
using Content.Shared.CCVar;
using Robust.Shared.Network;

namespace Content.Server._WH40K.PersistentInventory;

public sealed partial class PersistentInventoryAdminEui
{
    private string DescribeOverview(NetUserId user, PersistentInventorySnapshotHeader? header, bool busy)
    {
        var cache = _inventory.Get(user);
        var report = new StringBuilder();
        report.AppendLine($"{_username} / {user}");
        report.AppendLine($"Rollout: {PersistentInventoryRollout.GetMode(_configuration)} / {PersistentInventoryRollout.GetDecision(_configuration, user)}");
        report.AppendLine($"Cache: {cache.Status}; error: {cache.ErrorCode ?? "—"}");
        report.AppendLine($"Busy: {busy}; pending life loss: {_inventory.IsLifeLossPending(user)}");
        report.AppendLine($"Current server epoch: {_inventory.ServerEpoch}");
        report.AppendLine($"Save cooldown: {_configuration.GetCVar(CCVars.Wh40kPersistentInventorySaveCooldownSeconds)} s; " +
                          $"disconnect timeout: {_configuration.GetCVar(CCVars.Wh40kPersistentInventoryDisconnectDeleteDelaySeconds)} s");
        report.AppendLine($"Limits: {_entities.System<PersistentInventorySnapshotSerializer>().GetConfiguredLimits()}");
        if (_players.TryGetSessionById(user, out var session))
        {
            report.AppendLine($"Session: {session.Status}; body: {session.AttachedEntity?.ToString() ?? "—"}");
            if (session.AttachedEntity is { } body && _entities.TryGetComponent(body, out PersistentInventoryBoundLifeComponent? life))
                report.AppendLine($"Body binding: snapshot={life.SnapshotId}; life={life.LifeId}; " +
                                  $"loss started={life.LifeLossStarted}; suppress deletion={life.SuppressBodyDeletion}");
            if (session.AttachedEntity is { } owner && _entities.TryGetComponent(owner, out PersistentInventoryOperationComponent? operation))
                report.AppendLine($"Body lock: {operation.OperationId}; account={operation.UserId}");
        }
        else
            report.AppendLine(Text("persistentinv-admin-offline"));

        if (header == null)
        {
            report.AppendLine(Text("persistentinv-admin-no-snapshot"));
            return report.ToString();
        }
        report.AppendLine();
        report.AppendLine($"State: {header.State}; verified: {header.VerifiedState}; save phase: {header.SavePhase}");
        report.AppendLine($"Revision: {header.Revision}; operation: {header.OperationId}");
        report.AppendLine($"Current: {header.CurrentVerified?.SnapshotId.ToString() ?? "—"}");
        report.AppendLine($"Last known good: {header.LastKnownGood?.SnapshotId.ToString() ?? "—"}");
        report.AppendLine($"Staging: {header.Staging?.SnapshotId.ToString() ?? "—"}");
        report.AppendLine($"Bound epoch: {header.ServerEpoch?.ToString() ?? "—"}; staging epoch: {header.StagingServerEpoch?.ToString() ?? "—"}");
        report.AppendLine($"Life: {header.LifeId?.ToString() ?? "—"}");
        report.AppendLine($"Invalidation: {header.InvalidationReason}; loss: {header.LossReason}; quarantine: {header.QuarantineReason}");
        report.AppendLine($"Reason: {header.ReasonDetails ?? "—"}");
        report.AppendLine($"Created: {Utc(header.CreatedAt)}; updated: {Utc(header.UpdatedAt)}");
        report.AppendLine($"Restored: {Utc(header.RestoredAt)}; invalidated: {Utc(header.InvalidatedAt)}; lost: {Utc(header.LostAt)}");
        report.AppendLine($"World cleanup authorized: {Utc(header.WorldCleanupAuthorizedAt)}");
        return report.ToString();
    }

    private static string DescribeRevisionLabel(PersistentInventoryRevisionMetadata revision,
        PersistentInventorySnapshotHeader? header, PersistentInventorySnapshotId? lost)
    {
        var tags = new List<string>();
        if (header?.CurrentVerified?.SnapshotId == revision.SnapshotId) tags.Add("Current");
        if (header?.LastKnownGood?.SnapshotId == revision.SnapshotId) tags.Add("Previous");
        if (header?.Staging?.SnapshotId == revision.SnapshotId) tags.Add("Staging");
        if (lost == revision.SnapshotId) tags.Add("Lost");
        return $"{string.Join("/", tags)} {Utc(revision.SavedAt)} | {revision.ItemCount}/{revision.EntityCount} | {revision.SnapshotId.ToString()[..8]}";
    }

    private static string DescribeHistory(IReadOnlyList<PersistentInventoryAuditRecord> history)
    {
        var report = new StringBuilder();
        foreach (var entry in history)
        {
            report.AppendLine($"#{entry.Id} {Utc(entry.CreatedAt)} {entry.Action}: {entry.OldState} → {entry.NewState}");
            report.AppendLine($"  revision={entry.Revision}; snapshot={entry.SnapshotId}; operation={entry.OperationId}");
            report.AppendLine($"  actor={entry.Actor} ({entry.ActorUserId}); roots={entry.ItemCount}; entities={entry.EntityCount}; " +
                              $"bytes={entry.CompressedBytes}/{entry.UncompressedBytes}");
            report.AppendLine($"  {entry.Reason ?? "—"}");
            report.AppendLine();
        }
        return report.ToString();
    }

    private void DescribeSnapshot(PersistentInventoryStoredRevision stored)
    {
        var metadata = stored.Metadata;
        var report = new StringBuilder();
        report.AppendLine($"Snapshot: {metadata.SnapshotId}; operation: {metadata.OperationId}");
        report.AppendLine($"Profile: {metadata.CapturedProfileName ?? "—"}; role: {metadata.CapturedRoleId ?? "—"}");
        report.AppendLine($"Schema: {metadata.SchemaVersion}; policy: {metadata.PolicyId}");
        report.AppendLine($"Roots: {metadata.ItemCount}; entities: {metadata.EntityCount}; bytes: {metadata.CompressedBytes}/{metadata.UncompressedBytes}");
        report.AppendLine($"Created: {Utc(metadata.CreatedAt)}; saved: {Utc(metadata.SavedAt)}");
        report.AppendLine($"SHA256: {Convert.ToHexString(metadata.PayloadSha256)}");
        try
        {
            var serializer = _entities.System<PersistentInventorySnapshotSerializer>();
            var payload = PersistentInventoryPayloadCodec.Unpack(stored.Payload, metadata.PayloadSha256, serializer.GetConfiguredLimits());
            var preparation = serializer.PrepareForRestore(payload);
            report.AppendLine(Text("persistentinv-admin-payload-ok"));
            if (preparation.MigrationActions.Any(action => action.StartsWith("legacy-resource-state:", StringComparison.Ordinal)))
                report.AppendLine(Text("persistentinv-admin-legacy-resources"));
            foreach (var action in preparation.MigrationActions)
                report.AppendLine($"Migration: {action}");
            var parents = payload.Entities.SelectMany(parent => parent.Children.Select(child => (Parent: parent.EntityId, Child: child)))
                .ToDictionary(pair => pair.Child.EntityId);
            var roots = payload.Roots.ToDictionary(root => root.EntityId);
            var budget = 256 * 1024;
            var items = new List<PersistentInventoryAdminItem>();
            foreach (var entity in payload.Entities.OrderBy(entity => entity.EntityId))
            {
                var location = roots.TryGetValue(entity.EntityId, out var root) ? $"{root.Kind}: {root.Name}" :
                    parents.TryGetValue(entity.EntityId, out var parent) ? $"#{parent.Parent} / {parent.Child.ContainerId}[{parent.Child.Index}]" : "—";
                var details = new StringBuilder($"#{entity.EntityId} {entity.PrototypeId}\n{location}\n\n");
                foreach (var component in entity.Components.OrderBy(component => component.ComponentId, StringComparer.Ordinal))
                {
                    details.AppendLine(component.ComponentId);
                    foreach (var field in component.Fields.OrderBy(field => field.Key, StringComparer.Ordinal))
                        details.AppendLine($"  {field.Key}: {field.Value[..Math.Min(field.Value.Length, 1024)]}{(field.Value.Length > 1024 ? " …" : "")}");
                }
                foreach (var child in entity.Children)
                    details.AppendLine($"Child #{child.EntityId}: {child.ContainerId}[{child.Index}], location={child.StorageLocation}");
                var text = details.ToString();
                var length = Math.Min(Math.Min(text.Length, 16384), budget);
                budget -= length;
                if (length != text.Length)
                    text = text[..length] + "\n" + Text("persistentinv-admin-details-truncated");
                items.Add(new(entity.EntityId, entity.PrototypeId, location, text));
            }
            _view.Items = items.ToArray();
        }
        catch (Exception error)
        {
            report.AppendLine(Text("persistentinv-admin-validation-failed", ("error", error.Message)));
        }
        _view.SnapshotDetails = report.ToString();
    }

    private static string Utc(DateTime? time) => time?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "—";
}
