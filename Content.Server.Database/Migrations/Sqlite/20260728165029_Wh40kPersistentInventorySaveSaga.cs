using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kPersistentInventorySaveSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "save_phase",
                table: "wh40k_persistent_inventory",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "staging_server_epoch",
                table: "wh40k_persistent_inventory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "world_cleanup_authorized_at",
                table: "wh40k_persistent_inventory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "PersistentInventorySavePhaseNonNegative",
                table: "wh40k_persistent_inventory",
                sql: "save_phase >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "PersistentInventorySavePhaseNonNegative",
                table: "wh40k_persistent_inventory");

            migrationBuilder.DropColumn(
                name: "save_phase",
                table: "wh40k_persistent_inventory");

            migrationBuilder.DropColumn(
                name: "staging_server_epoch",
                table: "wh40k_persistent_inventory");

            migrationBuilder.DropColumn(
                name: "world_cleanup_authorized_at",
                table: "wh40k_persistent_inventory");
        }
    }
}
