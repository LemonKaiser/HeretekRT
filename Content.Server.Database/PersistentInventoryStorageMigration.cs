using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database;

internal static class PersistentInventoryStorageMigration
{
    internal static void Backfill(MigrationBuilder migration)
    {
        var expiry = migration.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite"
            ? "datetime(a.created_at, '+72 hours')" : "a.created_at + interval '72 hours'";
        migration.Sql($"""
            INSERT INTO wh40k_persistent_inventory_storage
                (user_id, lost_snapshot_id, lost_expires_at, last_counted_revision, pruned_events, saves, losses, recoveries)
            SELECT p.user_id,
                (SELECT a.snapshot_id FROM wh40k_persistent_inventory_audit a
                 JOIN wh40k_persistent_inventory_revision r ON r.user_id = a.user_id AND r.snapshot_id = a.snapshot_id
                 WHERE a.user_id = p.user_id AND a.action = 6 AND a.old_state = 3 AND a.new_state = 5 ORDER BY a.wh40k_persistent_inventory_audit_id DESC LIMIT 1),
                (SELECT {expiry} FROM wh40k_persistent_inventory_audit a
                 JOIN wh40k_persistent_inventory_revision r ON r.user_id = a.user_id AND r.snapshot_id = a.snapshot_id
                 WHERE a.user_id = p.user_id AND a.action = 6 AND a.old_state = 3 AND a.new_state = 5 ORDER BY a.wh40k_persistent_inventory_audit_id DESC LIMIT 1),
                p.revision, 0,
                (SELECT COUNT(*) FROM wh40k_persistent_inventory_audit a WHERE a.user_id = p.user_id AND a.action = 2),
                (SELECT COUNT(*) FROM wh40k_persistent_inventory_audit a WHERE a.user_id = p.user_id AND a.action IN (5, 6)),
                (SELECT COUNT(*) FROM wh40k_persistent_inventory_audit a WHERE a.user_id = p.user_id AND a.action = 9)
            FROM wh40k_persistent_inventory p;
            """);
        migration.Sql("""
            UPDATE wh40k_persistent_inventory_revision
            SET is_verified = TRUE
            WHERE EXISTS (SELECT 1 FROM wh40k_persistent_inventory_audit a
                WHERE a.user_id = wh40k_persistent_inventory_revision.user_id
                AND a.snapshot_id = wh40k_persistent_inventory_revision.snapshot_id AND a.action IN (2, 10));
            INSERT INTO wh40k_persistent_inventory_receipt (user_id, operation_id, action, revision, new_state, snapshot_id)
            SELECT a.user_id, a.operation_id, a.action, a.revision, a.new_state, a.snapshot_id
            FROM wh40k_persistent_inventory_audit a JOIN wh40k_persistent_inventory p ON p.user_id = a.user_id
            WHERE a.revision > p.revision - 64 OR a.operation_id = p.operation_id;
            """);
        if (migration.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            migration.Sql("""
                ALTER TABLE wh40k_persistent_inventory SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_vacuum_threshold = 50);
                ALTER TABLE wh40k_persistent_inventory_revision SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_vacuum_threshold = 50);
                ALTER TABLE wh40k_persistent_inventory_blob SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_vacuum_threshold = 50,
                    toast.autovacuum_vacuum_scale_factor = 0.02, toast.autovacuum_vacuum_threshold = 50);
                ALTER TABLE wh40k_persistent_inventory_audit SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_vacuum_threshold = 50);
                ALTER TABLE wh40k_persistent_inventory_receipt SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_vacuum_threshold = 50);
                ALTER TABLE wh40k_persistent_inventory_storage SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_vacuum_threshold = 50);
                """);
        }
    }

    internal static void RestoreInlinePayloads(MigrationBuilder migration)
    {
        if (migration.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            migration.Sql("""
                ALTER TABLE wh40k_persistent_inventory RESET (autovacuum_vacuum_scale_factor, autovacuum_vacuum_threshold);
                ALTER TABLE wh40k_persistent_inventory_revision RESET (autovacuum_vacuum_scale_factor, autovacuum_vacuum_threshold);
                ALTER TABLE wh40k_persistent_inventory_audit RESET (autovacuum_vacuum_scale_factor, autovacuum_vacuum_threshold);
                """);
        migration.Sql("""
            UPDATE wh40k_persistent_inventory_revision
            SET payload = (SELECT b.payload FROM wh40k_persistent_inventory_blob b
                WHERE b.wh40k_persistent_inventory_blob_id = wh40k_persistent_inventory_revision.blob_id
                AND b.user_id = wh40k_persistent_inventory_revision.user_id)
            WHERE blob_id IS NOT NULL;
            """);
    }
}
