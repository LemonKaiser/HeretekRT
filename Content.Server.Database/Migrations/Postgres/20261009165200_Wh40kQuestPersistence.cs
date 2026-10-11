using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Wh40kQuestPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_quest_owner",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_owner", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_audit",
                columns: table => new
                {
                    wh40k_quest_audit_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_audit", x => x.wh40k_quest_audit_id);
                    table.ForeignKey(
                        name: "FK_wh40k_quest_audit_wh40k_quest_owner_wh40k_quest_owner_key",
                        column: x => x.owner_key,
                        principalTable: "wh40k_quest_owner",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_offer",
                columns: table => new
                {
                    wh40k_quest_offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    definition_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    terms_json = table.Column<string>(type: "character varying(131072)", maxLength: 131072, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    active_limit = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_offer", x => x.wh40k_quest_offer_id);
                    table.ForeignKey(
                        name: "FK_wh40k_quest_offer_wh40k_quest_owner_wh40k_quest_owner_key",
                        column: x => x.owner_key,
                        principalTable: "wh40k_quest_owner",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_operation",
                columns: table => new
                {
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result = table.Column<int>(type: "integer", nullable: false),
                    quest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_operation", x => new { x.owner_key, x.operation_id });
                    table.ForeignKey(
                        name: "FK_wh40k_quest_operation_wh40k_quest_owner_wh40k_quest_owner_k~",
                        column: x => x.owner_key,
                        principalTable: "wh40k_quest_owner",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_instance",
                columns: table => new
                {
                    wh40k_quest_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    definition_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    snapshot_json = table.Column<string>(type: "character varying(131072)", maxLength: 131072, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_generation = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_instance", x => x.wh40k_quest_instance_id);
                    table.ForeignKey(
                        name: "FK_wh40k_quest_instance_wh40k_quest_offer_wh40k_quest_offer_id",
                        column: x => x.offer_id,
                        principalTable: "wh40k_quest_offer",
                        principalColumn: "wh40k_quest_offer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wh40k_quest_instance_wh40k_quest_owner_wh40k_quest_owner_key",
                        column: x => x.owner_key,
                        principalTable: "wh40k_quest_owner",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_objective",
                columns: table => new
                {
                    quest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    required_count = table.Column<int>(type: "integer", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_objective", x => new { x.quest_id, x.objective_id });
                    table.CheckConstraint("quest_objective_count", "count >= 0 AND count <= required_count AND required_count > 0");
                    table.ForeignKey(
                        name: "FK_wh40k_quest_objective_wh40k_quest_instance_wh40k_quest_inst~",
                        column: x => x.quest_id,
                        principalTable: "wh40k_quest_instance",
                        principalColumn: "wh40k_quest_instance_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_reward_intent",
                columns: table => new
                {
                    quest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recipient = table.Column<Guid>(type: "uuid", nullable: false),
                    reward_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload_json = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_reward_intent", x => new { x.quest_id, x.entry_id, x.recipient });
                    table.ForeignKey(
                        name: "FK_wh40k_quest_reward_intent_wh40k_quest_instance_wh40k_quest_~",
                        column: x => x.quest_id,
                        principalTable: "wh40k_quest_instance",
                        principalColumn: "wh40k_quest_instance_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_inbox",
                columns: table => new
                {
                    quest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    retry_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fact_json = table.Column<string>(type: "character varying(32768)", maxLength: 32768, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_inbox", x => new { x.quest_id, x.objective_id, x.event_id });
                    table.ForeignKey(
                        name: "FK_wh40k_quest_inbox_wh40k_quest_objective_quest_id_objective_~",
                        columns: x => new { x.quest_id, x.objective_id },
                        principalTable: "wh40k_quest_objective",
                        principalColumns: new[] { "quest_id", "objective_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_receipt",
                columns: table => new
                {
                    quest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_receipt", x => new { x.quest_id, x.objective_id, x.event_id });
                    table.ForeignKey(
                        name: "FK_wh40k_quest_receipt_wh40k_quest_objective_quest_id_objectiv~",
                        columns: x => new { x.quest_id, x.objective_id },
                        principalTable: "wh40k_quest_objective",
                        principalColumns: new[] { "quest_id", "objective_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wh40k_quest_submission",
                columns: table => new
                {
                    wh40k_quest_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    payload_json = table.Column<string>(type: "character varying(1048576)", maxLength: 1048576, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_quest_submission", x => x.wh40k_quest_submission_id);
                    table.CheckConstraint("quest_submission_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_wh40k_quest_submission_wh40k_quest_objective_wh40k_quest_ob~",
                        columns: x => new { x.quest_id, x.objective_id },
                        principalTable: "wh40k_quest_objective",
                        principalColumns: new[] { "quest_id", "objective_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_audit_owner_key_wh40k_quest_audit_id",
                table: "wh40k_quest_audit",
                columns: new[] { "owner_key", "wh40k_quest_audit_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_inbox_state_retry_after_occurred_at",
                table: "wh40k_quest_inbox",
                columns: new[] { "state", "retry_after", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_instance_offer_id",
                table: "wh40k_quest_instance",
                column: "offer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_instance_owner_key_definition_id_closed_at",
                table: "wh40k_quest_instance",
                columns: new[] { "owner_key", "definition_id", "closed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_instance_owner_key_state",
                table: "wh40k_quest_instance",
                columns: new[] { "owner_key", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_offer_owner_key_state_expires_at",
                table: "wh40k_quest_offer",
                columns: new[] { "owner_key", "state", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_receipt_quest_id_objective_id_target_token",
                table: "wh40k_quest_receipt",
                columns: new[] { "quest_id", "objective_id", "target_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_reward_intent_state",
                table: "wh40k_quest_reward_intent",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_quest_submission_quest_id_objective_id_state",
                table: "wh40k_quest_submission",
                columns: new[] { "quest_id", "objective_id", "state" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_quest_audit");

            migrationBuilder.DropTable(
                name: "wh40k_quest_inbox");

            migrationBuilder.DropTable(
                name: "wh40k_quest_operation");

            migrationBuilder.DropTable(
                name: "wh40k_quest_receipt");

            migrationBuilder.DropTable(
                name: "wh40k_quest_reward_intent");

            migrationBuilder.DropTable(
                name: "wh40k_quest_submission");

            migrationBuilder.DropTable(
                name: "wh40k_quest_objective");

            migrationBuilder.DropTable(
                name: "wh40k_quest_instance");

            migrationBuilder.DropTable(
                name: "wh40k_quest_offer");

            migrationBuilder.DropTable(
                name: "wh40k_quest_owner");

        }
    }
}
