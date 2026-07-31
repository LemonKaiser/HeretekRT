using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Wh40kPersistentInventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_persistent_inventory",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    verified_state = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_known_good_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    staging_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    server_epoch = table.Column<Guid>(type: "uuid", nullable: true),
                    life_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invalidation_reason = table.Column<int>(type: "integer", nullable: false),
                    loss_reason = table.Column<int>(type: "integer", nullable: false),
                    quarantine_reason = table.Column<int>(type: "integer", nullable: false),
                    reason_details = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    restored_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invalidated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lost_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_persistent_inventory", x => x.user_id);
                    table.CheckConstraint("PersistentInventoryRevisionNonNegative", "revision >= 0");
                    table.CheckConstraint("PersistentInventoryVerifiedStateNonNegative", "verified_state >= 0");
                    table.ForeignKey(
                        name: "FK_wh40k_persistent_inventory_player_player_id",
                        column: x => x.user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_persistent_inventory_audit",
                columns: table => new
                {
                    wh40k_persistent_inventory_audit_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    old_state = table.Column<int>(type: "integer", nullable: false),
                    new_state = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    entity_count = table.Column<int>(type: "integer", nullable: false),
                    uncompressed_bytes = table.Column<int>(type: "integer", nullable: false),
                    compressed_bytes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_persistent_inventory_audit", x => x.wh40k_persistent_inventory_audit_id);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_persistent_inventory_revision",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    policy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    captured_role_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    captured_profile_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    payload_sha256 = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    entity_count = table.Column<int>(type: "integer", nullable: false),
                    uncompressed_bytes = table.Column<int>(type: "integer", nullable: false),
                    compressed_bytes = table.Column<int>(type: "integer", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_persistent_inventory_revision", x => x.snapshot_id);
                    table.CheckConstraint("PersistentInventoryCompressedBytesNonNegative", "compressed_bytes >= 0");
                    table.CheckConstraint("PersistentInventoryEntityCountNonNegative", "entity_count >= 0");
                    table.CheckConstraint("PersistentInventoryItemCountNonNegative", "item_count >= 0");
                    table.CheckConstraint("PersistentInventoryUncompressedBytesNonNegative", "uncompressed_bytes >= 0");
                    table.ForeignKey(
                        name: "FK_wh40k_persistent_inventory_revision_wh40k_persistent_invent~",
                        column: x => x.user_id,
                        principalTable: "wh40k_persistent_inventory",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_lost_at",
                table: "wh40k_persistent_inventory",
                column: "lost_at");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_operation_id",
                table: "wh40k_persistent_inventory",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_state",
                table: "wh40k_persistent_inventory",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_updated_at",
                table: "wh40k_persistent_inventory",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_audit_user_id_created_at",
                table: "wh40k_persistent_inventory_audit",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_audit_user_id_operation_id_action",
                table: "wh40k_persistent_inventory_audit",
                columns: new[] { "user_id", "operation_id", "action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_revision_saved_at",
                table: "wh40k_persistent_inventory_revision",
                column: "saved_at");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_revision_user_id",
                table: "wh40k_persistent_inventory_revision",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_revision_user_id_operation_id",
                table: "wh40k_persistent_inventory_revision",
                columns: new[] { "user_id", "operation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_persistent_inventory_audit");

            migrationBuilder.DropTable(
                name: "wh40k_persistent_inventory_revision");

            migrationBuilder.DropTable(
                name: "wh40k_persistent_inventory");
        }
    }
}
