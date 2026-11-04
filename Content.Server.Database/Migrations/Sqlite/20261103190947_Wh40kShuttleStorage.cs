using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kShuttleStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_shuttle_slot",
                columns: table => new
                {
                    owner = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    operation = table.Column<Guid>(type: "TEXT", nullable: false),
                    ship = table.Column<Guid>(type: "TEXT", nullable: false),
                    epoch = table.Column<Guid>(type: "TEXT", nullable: false),
                    state = table.Column<byte>(type: "INTEGER", nullable: false),
                    authorized = table.Column<bool>(type: "INTEGER", nullable: false),
                    result = table.Column<int>(type: "INTEGER", nullable: false),
                    schema = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: false),
                    height = table.Column<int>(type: "INTEGER", nullable: false),
                    entities = table.Column<int>(type: "INTEGER", nullable: false),
                    bytes = table.Column<int>(type: "INTEGER", nullable: false),
                    saved_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    payload = table.Column<byte[]>(type: "BLOB", nullable: true),
                    sha256 = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_shuttle_slot", x => x.owner);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_shuttle_slot");
        }
    }
}
