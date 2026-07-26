using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kRpgAccountCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_account_rpg_foundation",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    homeworld_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    origin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    class_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    initial_portrait_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    initial_characteristic_points = table.Column<byte[]>(type: "jsonb", nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_account_rpg_foundation", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_wh40k_account_rpg_foundation_player_player_id",
                        column: x => x.user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_account_attribute_purchase",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    characteristic = table.Column<int>(type: "INTEGER", nullable: false),
                    purchased_points = table.Column<int>(type: "INTEGER", nullable: false),
                    first_purchased_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_account_attribute_purchase", x => new { x.user_id, x.characteristic });
                    table.CheckConstraint("PurchasedPointsNonNegative", "purchased_points >= 0");
                    table.ForeignKey(
                        name: "FK_wh40k_account_attribute_purchase_wh40k_account_rpg_foundation_user_id",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_account_rpg_progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    schema_version = table.Column<int>(type: "INTEGER", nullable: false),
                    experience_tenths = table.Column<long>(type: "INTEGER", nullable: false),
                    level = table.Column<int>(type: "INTEGER", nullable: false),
                    unspent_development_points = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_account_rpg_progress", x => x.user_id);
                    table.CheckConstraint("DevelopmentPointsNonNegative", "unspent_development_points >= 0");
                    table.CheckConstraint("ExperienceTenthsNonNegative", "experience_tenths >= 0");
                    table.CheckConstraint("RpgLevelRange", "level >= 1 AND level <= 100");
                    table.CheckConstraint("RpgRevisionNonNegative", "revision >= 0");
                    table.ForeignKey(
                        name: "FK_wh40k_account_rpg_progress_wh40k_account_rpg_foundation_wh40k_account_rpg_foundation_user_id",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_experience_ledger",
                columns: table => new
                {
                    wh40k_experience_ledger_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reward_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    source_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    amount_tenths = table.Column<long>(type: "INTEGER", nullable: false),
                    round_id = table.Column<int>(type: "INTEGER", nullable: true),
                    issuer_entity = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    context_json = table.Column<byte[]>(type: "jsonb", nullable: true),
                    awarded_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    balance_version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_experience_ledger", x => x.wh40k_experience_ledger_id);
                    table.ForeignKey(
                        name: "FK_wh40k_experience_ledger_wh40k_account_rpg_foundation_wh40k_account_rpg_foundation_user_id",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_party",
                columns: table => new
                {
                    wh40k_party_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    leader_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_party", x => x.wh40k_party_id);
                    table.CheckConstraint("PartyExpirationAfterCreation", "expires_at > created_at");
                    table.CheckConstraint("PartyRevisionNonNegative", "revision >= 0");
                    table.ForeignKey(
                        name: "FK_wh40k_party_wh40k_account_rpg_foundation_wh40k_account_rpg_foundation_user_id",
                        column: x => x.leader_user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_party_preference",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    allow_invites = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_party_preference", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_wh40k_party_preference_wh40k_account_rpg_foundation_wh40k_account_rpg_foundation_user_id",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_reward_delivery",
                columns: table => new
                {
                    wh40k_reward_delivery_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reward_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    entry_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    reward_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    prototype_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    context_json = table.Column<byte[]>(type: "jsonb", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_reward_delivery", x => x.wh40k_reward_delivery_id);
                    table.CheckConstraint("RewardAmountPositive", "amount > 0");
                    table.CheckConstraint("RewardAttemptCountNonNegative", "attempt_count >= 0");
                    table.ForeignKey(
                        name: "FK_wh40k_reward_delivery_wh40k_account_rpg_foundation_wh40k_account_rpg_foundation_user_id",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_party_member",
                columns: table => new
                {
                    party_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    joined_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_party_member", x => new { x.party_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_wh40k_party_member_wh40k_account_rpg_foundation_user_id",
                        column: x => x.user_id,
                        principalTable: "wh40k_account_rpg_foundation",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wh40k_party_member_wh40k_party_wh40k_party_id",
                        column: x => x.party_id,
                        principalTable: "wh40k_party",
                        principalColumn: "wh40k_party_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_experience_ledger_user_id_awarded_at",
                table: "wh40k_experience_ledger",
                columns: new[] { "user_id", "awarded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_experience_ledger_user_id_reward_id",
                table: "wh40k_experience_ledger",
                columns: new[] { "user_id", "reward_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_party_expires_at",
                table: "wh40k_party",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_party_leader_user_id",
                table: "wh40k_party",
                column: "leader_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_party_member_user_id",
                table: "wh40k_party_member",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_reward_delivery_user_id_reward_id_entry_id",
                table: "wh40k_reward_delivery",
                columns: new[] { "user_id", "reward_id", "entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_reward_delivery_user_id_status",
                table: "wh40k_reward_delivery",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_account_attribute_purchase");

            migrationBuilder.DropTable(
                name: "wh40k_account_rpg_progress");

            migrationBuilder.DropTable(
                name: "wh40k_experience_ledger");

            migrationBuilder.DropTable(
                name: "wh40k_party_member");

            migrationBuilder.DropTable(
                name: "wh40k_party_preference");

            migrationBuilder.DropTable(
                name: "wh40k_reward_delivery");

            migrationBuilder.DropTable(
                name: "wh40k_party");

            migrationBuilder.DropTable(
                name: "wh40k_account_rpg_foundation");
        }
    }
}
