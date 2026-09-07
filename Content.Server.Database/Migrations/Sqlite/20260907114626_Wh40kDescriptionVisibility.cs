using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kDescriptionVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "share_links_flavor_text",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "share_ooc_flavor_text",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "share_preferences_flavor_text",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "share_links_flavor_text",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "share_ooc_flavor_text",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "share_preferences_flavor_text",
                table: "profile");
        }
    }
}
