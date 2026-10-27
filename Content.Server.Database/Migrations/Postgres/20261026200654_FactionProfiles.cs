using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
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
                    company_id = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    admission = table.Column<string>(type: "text", nullable: false),
                    sections_json = table.Column<string>(type: "text", nullable: false),
                    leader_title = table.Column<string>(type: "text", nullable: false),
                    officer_title = table.Column<string>(type: "text", nullable: false),
                    member_title = table.Column<string>(type: "text", nullable: false)
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
