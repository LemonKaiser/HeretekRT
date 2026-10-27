using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class FactionProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "faction_profiles",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    admission = table.Column<string>(type: "TEXT", nullable: false),
                    sections_json = table.Column<string>(type: "TEXT", nullable: false),
                    leader_title = table.Column<string>(type: "TEXT", nullable: false),
                    officer_title = table.Column<string>(type: "TEXT", nullable: false),
                    member_title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faction_profiles", x => x.company_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faction_profiles");
        }
    }
}
