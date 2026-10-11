using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Wh40kMerchantSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wh40k_merchant_sale",
                columns: table => new
                {
                    token = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation = table.Column<Guid>(type: "TEXT", nullable: false),
                    merchant = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    product = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    price = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", maxLength: 131072, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wh40k_merchant_sale", x => x.token);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_merchant_sale_operation",
                table: "wh40k_merchant_sale",
                column: "operation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wh40k_merchant_sale_user_id_state",
                table: "wh40k_merchant_sale",
                columns: new[] { "user_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wh40k_merchant_sale");
        }
    }
}
