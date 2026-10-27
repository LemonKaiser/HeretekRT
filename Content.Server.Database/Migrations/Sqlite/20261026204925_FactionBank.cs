using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class FactionBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "faction_bank_accounts",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    balance = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faction_bank_accounts", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "faction_bank_tax_counters",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    remainder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faction_bank_tax_counters", x => new { x.company_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "faction_bank_transactions",
                columns: table => new
                {
                    faction_bank_transactions_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    actor = table.Column<Guid>(type: "TEXT", nullable: false),
                    recipient = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    gross = table.Column<int>(type: "INTEGER", nullable: false),
                    amount = table.Column<int>(type: "INTEGER", nullable: false),
                    memo = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    pending = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faction_bank_transactions", x => x.faction_bank_transactions_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_faction_bank_transactions_company_id_created_at",
                table: "faction_bank_transactions",
                columns: new[] { "company_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_faction_bank_transactions_recipient_pending",
                table: "faction_bank_transactions",
                columns: new[] { "recipient", "pending" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faction_bank_accounts");

            migrationBuilder.DropTable(
                name: "faction_bank_tax_counters");

            migrationBuilder.DropTable(
                name: "faction_bank_transactions");
        }
    }
}
