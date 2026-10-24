using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Wh40kPersistentInventoryStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "blob_id",
                table: "wh40k_persistent_inventory_revision",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_verified",
                table: "wh40k_persistent_inventory_revision",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "wh40k_persistent_inventory_blob",
                columns: table => new
                {
                    wh40k_persistent_inventory_blob_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_persistent_inventory_blob", x => x.wh40k_persistent_inventory_blob_id);
                    table.ForeignKey(
                        name: "FK_wh40k_persistent_inventory_blob_wh40k_persistent_inventory_~",
                        column: x => x.user_id,
                        principalTable: "wh40k_persistent_inventory",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_persistent_inventory_receipt",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    new_state = table.Column<int>(type: "integer", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_persistent_inventory_receipt", x => new { x.user_id, x.operation_id, x.action });
                    table.ForeignKey(
                        name: "FK_wh40k_persistent_inventory_receipt_wh40k_persistent_invento~",
                        column: x => x.user_id,
                        principalTable: "wh40k_persistent_inventory",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_persistent_inventory_storage",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lost_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lost_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_counted_revision = table.Column<long>(type: "bigint", nullable: false),
                    pruned_events = table.Column<long>(type: "bigint", nullable: false),
                    saves = table.Column<long>(type: "bigint", nullable: false),
                    losses = table.Column<long>(type: "bigint", nullable: false),
                    recoveries = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_persistent_inventory_storage", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_wh40k_persistent_inventory_storage_wh40k_persistent_invento~",
                        column: x => x.user_id,
                        principalTable: "wh40k_persistent_inventory",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_blob_user_id_sha256",
                table: "wh40k_persistent_inventory_blob",
                columns: new[] { "user_id", "sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_receipt_user_id_revision",
                table: "wh40k_persistent_inventory_receipt",
                columns: new[] { "user_id", "revision" });
            PersistentInventoryStorageMigration.Backfill(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            PersistentInventoryStorageMigration.RestoreInlinePayloads(migrationBuilder);

            migrationBuilder.DropTable(
                name: "wh40k_persistent_inventory_blob");

            migrationBuilder.DropTable(
                name: "wh40k_persistent_inventory_receipt");

            migrationBuilder.DropTable(
                name: "wh40k_persistent_inventory_storage");

            migrationBuilder.DropColumn(
                name: "blob_id",
                table: "wh40k_persistent_inventory_revision");

            migrationBuilder.DropColumn(
                name: "is_verified",
                table: "wh40k_persistent_inventory_revision");
        }
    }
}
