using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kOnboardingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "wh40k_build",
                table: "profile",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wh40k_player_progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    act_stage = table.Column<int>(type: "INTEGER", nullable: false),
                    onboarding_status = table.Column<int>(type: "INTEGER", nullable: false),
                    onboarding_profile_slot = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_player_progress", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_player_progress");

            migrationBuilder.DropColumn(
                name: "wh40k_build",
                table: "profile");
        }
    }
}
