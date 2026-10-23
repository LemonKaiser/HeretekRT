using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
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
                    faction = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: true),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: true),
                    prototype = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    name = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    description = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false)
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
