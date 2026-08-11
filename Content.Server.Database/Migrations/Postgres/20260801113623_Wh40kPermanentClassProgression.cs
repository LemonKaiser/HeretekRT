using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Wh40kPermanentClassProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_account_class_audit",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    previous_class_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    new_class_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_skill_ids = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    new_skill_ids = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_account_class_audit", x => x.operation_id);
                    table.ForeignKey(
                        name: "FK_wh40k_account_class_audit_wh40k_account_rpg_foundation_wh40~",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_account_class_progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tree_version = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_account_class_progress", x => x.user_id);
                    table.CheckConstraint("ClassTreeRevisionNonNegative", "revision >= 0");
                    table.CheckConstraint("ClassTreeVersionPositive", "tree_version > 0");
                    table.ForeignKey(
                        name: "FK_wh40k_account_class_progress_wh40k_account_rpg_foundation_w~",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_account_class_skill",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    purchased_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_account_class_skill", x => new { x.user_id, x.skill_id });
                    table.ForeignKey(
                        name: "FK_wh40k_account_class_skill_wh40k_account_class_progress_user~",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_class_progress",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO wh40k_account_class_progress
                    (user_id, tree_version, revision, created_at, updated_at)
                SELECT user_id, 1, 0, created_at, created_at
                FROM wh40k_account_rpg_foundation
                """);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_account_class_audit_user_id_created_at",
                table: "wh40k_account_class_audit",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_account_class_audit");

            migrationBuilder.DropTable(
                name: "wh40k_account_class_skill");

            migrationBuilder.DropTable(
                name: "wh40k_account_class_progress");
        }
    }
}
