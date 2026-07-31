using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Wh40kPersistentInventoryLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_persistent_inventory_server_epoch",
                columns: table => new
                {
                    server_epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    clean_shutdown_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_persistent_inventory_server_epoch", x => x.server_epoch);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_persistent_inventory_server_epoch_started_at",
                table: "wh40k_persistent_inventory_server_epoch",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_persistent_inventory_server_epoch");
        }
    }
}
