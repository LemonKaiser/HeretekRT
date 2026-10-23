using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kFactionStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_faction_storage_slot",
                columns: table => new
                {
                    faction = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    slot = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<Guid>(type: "TEXT", nullable: false),
                    payload = table.Column<byte[]>(type: "BLOB", nullable: true),
                    sha256 = table.Column<byte[]>(type: "BLOB", nullable: true),
                    prototype = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    name = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    description = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                    count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_faction_storage_slot", x => new { x.faction, x.slot });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_faction_storage_slot");
        }
    }
}
