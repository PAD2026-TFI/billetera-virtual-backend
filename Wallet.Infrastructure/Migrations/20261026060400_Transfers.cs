using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Transfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements");

            migrationBuilder.AddColumn<Guid>(
                name: "TransferId",
                table: "Movements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceWalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationWalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.CheckConstraint("CK_Transfers_Amount", "[Amount] > 0 AND [Amount] <= 780400");
                    table.CheckConstraint("CK_Transfers_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
                    table.CheckConstraint("CK_Transfers_Status", "[Status] = 'Completed'");
                    table.CheckConstraint("CK_Transfers_Wallets", "[SourceWalletId] <> [DestinationWalletId]");
                    table.ForeignKey(
                        name: "FK_Transfers_Wallets_DestinationWalletId",
                        column: x => x.DestinationWalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Wallets_SourceWalletId",
                        column: x => x.SourceWalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_TransferId_WalletId_Type",
                table: "Movements",
                columns: new[] { "TransferId", "WalletId", "Type" },
                unique: true,
                filter: "[TransferId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements",
                sql: "([DevelopmentFundingId] IS NOT NULL AND [TransferId] IS NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit') OR ([DevelopmentFundingId] IS NULL AND [TransferId] IS NOT NULL AND [Source] = 'Transfer' AND [Type] IN ('Debit', 'Credit'))");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_DestinationWalletId",
                table: "Transfers",
                column: "DestinationWalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_SourceWalletId_IdempotencyKey",
                table: "Transfers",
                columns: new[] { "SourceWalletId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_Transfers_TransferId",
                table: "Movements",
                column: "TransferId",
                principalTable: "Transfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_Transfers_TransferId",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropIndex(
                name: "IX_Movements_TransferId_WalletId_Type",
                table: "Movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "TransferId",
                table: "Movements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements",
                sql: "[DevelopmentFundingId] IS NOT NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit'");
        }
    }
}
