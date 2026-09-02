using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class RepairAdminRankHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The initial hierarchy migration was released with a default of zero.
            // Normalize already-migrated ranks before the range constraint is applied.
            migrationBuilder.Sql("UPDATE admin_rank SET hierarchy_level = 9 WHERE hierarchy_level < 1 OR hierarchy_level > 9");

            migrationBuilder.AlterColumn<byte>(
                name: "hierarchy_level",
                table: "admin_rank",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)9,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AddCheckConstraint(
                name: "AdminRankHierarchyLevelRange",
                table: "admin_rank",
                sql: "hierarchy_level >= 1 AND hierarchy_level <= 9");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "AdminRankHierarchyLevelRange",
                table: "admin_rank");

            migrationBuilder.AlterColumn<byte>(
                name: "hierarchy_level",
                table: "admin_rank",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldDefaultValue: (byte)9);
        }
    }
}
