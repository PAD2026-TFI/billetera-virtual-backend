using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentEntityAndB2BConsultation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PaymentLinks",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MerchantOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationWalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayerWalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    Method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CardBrand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CardLast4 = table.Column<string>(type: "char(4)", maxLength: 4, nullable: true),
                    CardHolderName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AuthorizationCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Payments_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
                    table.ForeignKey(
                        name: "FK_Payments_PaymentLinks_PaymentLinkId",
                        column: x => x.PaymentLinkId,
                        principalTable: "PaymentLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Transfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "Transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_PayerUserId",
                        column: x => x.PayerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_WalletOwners_MerchantOwnerId",
                        column: x => x.MerchantOwnerId,
                        principalTable: "WalletOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Wallets_DestinationWalletId",
                        column: x => x.DestinationWalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Wallets_PayerWalletId",
                        column: x => x.PayerWalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_DestinationWalletId",
                table: "Payments",
                column: "DestinationWalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantOwnerId_CreatedAt",
                table: "Payments",
                columns: new[] { "MerchantOwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantOwnerId_IdempotencyKey",
                table: "Payments",
                columns: new[] { "MerchantOwnerId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PayerUserId",
                table: "Payments",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PayerWalletId",
                table: "Payments",
                column: "PayerWalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentLinkId",
                table: "Payments",
                column: "PaymentLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TransferId",
                table: "Payments",
                column: "TransferId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PaymentLinks");
        }
    }
}
