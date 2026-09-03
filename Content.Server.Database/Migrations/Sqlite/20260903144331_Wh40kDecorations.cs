using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kDecorations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_decoration_selection",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selected_ghost_skin_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    selected_ooc_title_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    selected_ooc_name_color_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_decoration_selection", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_decoration_selection");
        }
    }
}
