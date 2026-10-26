using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DevelopmentFunding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevelopmentFundings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevelopmentFundings", x => x.Id);
                    table.CheckConstraint("CK_DevelopmentFundings_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_DevelopmentFundings_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
                    table.ForeignKey(
                        name: "FK_DevelopmentFundings_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DevelopmentFundingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                    table.CheckConstraint("CK_Movements_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Movements_BalanceAfter", "[BalanceAfter] >= 0");
                    table.CheckConstraint("CK_Movements_Source", "[DevelopmentFundingId] IS NOT NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit'");
                    table.ForeignKey(
                        name: "FK_Movements_DevelopmentFundings_DevelopmentFundingId",
                        column: x => x.DevelopmentFundingId,
                        principalTable: "DevelopmentFundings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DevelopmentFundings_IdempotencyKey",
                table: "DevelopmentFundings",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DevelopmentFundings_WalletId",
                table: "DevelopmentFundings",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_DevelopmentFundingId",
                table: "Movements",
                column: "DevelopmentFundingId",
                unique: true,
                filter: "[DevelopmentFundingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_WalletId_CreatedAt_Id",
                table: "Movements",
                columns: new[] { "WalletId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Movements");

            migrationBuilder.DropTable(
                name: "DevelopmentFundings");
        }
    }
}
