using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
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
                    owner = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<Guid>(type: "uuid", nullable: false),
                    ship = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<byte>(type: "smallint", nullable: false),
                    authorized = table.Column<bool>(type: "boolean", nullable: false),
                    result = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    entities = table.Column<int>(type: "integer", nullable: false),
                    bytes = table.Column<int>(type: "integer", nullable: false),
                    saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<byte[]>(type: "bytea", nullable: true),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: true)
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
