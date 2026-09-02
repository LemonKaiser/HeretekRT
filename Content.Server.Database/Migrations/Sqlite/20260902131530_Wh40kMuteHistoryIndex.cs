using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kMuteHistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_wh40k_mute_player_user_id_mute_time",
                table: "wh40k_mute",
                columns: new[] { "player_user_id", "mute_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wh40k_mute_player_user_id_mute_time",
                table: "wh40k_mute");
        }
    }
}
