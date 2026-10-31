using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationWalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MerchantOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLinks", x => x.Id);
                    table.CheckConstraint("CK_PaymentLinks_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_PaymentLinks_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
                    table.ForeignKey(
                        name: "FK_PaymentLinks_WalletOwners_MerchantOwnerId",
                        column: x => x.MerchantOwnerId,
                        principalTable: "WalletOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentLinks_Wallets_DestinationWalletId",
                        column: x => x.DestinationWalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_DestinationWalletId",
                table: "PaymentLinks",
                column: "DestinationWalletId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_MerchantOwnerId",
                table: "PaymentLinks",
                column: "MerchantOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_Token",
                table: "PaymentLinks",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentLinks");
        }
    }
}
