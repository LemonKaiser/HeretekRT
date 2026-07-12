using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class DialoguePersistentMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dialogue_persistent_memory",
                columns: table => new
                {
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    memory_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    data = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dialogue_persistent_memory", x => new { x.player_user_id, x.memory_key });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dialogue_persistent_memory");
        }
    }
}
