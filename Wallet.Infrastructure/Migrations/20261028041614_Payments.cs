using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements");

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                table: "Movements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PreferenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckoutAttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecoveryAttempts = table.Column<int>(type: "int", nullable: false),
                    RequiresReview = table.Column<bool>(type: "bit", nullable: false),
                    LastIssue = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Payments_Credit", "([Status] = 'Approved' AND [CreditedAt] IS NOT NULL AND [ProviderPaymentId] IS NOT NULL) OR ([Status] <> 'Approved' AND [CreditedAt] IS NULL)");
                    table.CheckConstraint("CK_Payments_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
                    table.CheckConstraint("CK_Payments_Status", "[Status] IN ('Created','Pending','Unknown','Approved','Rejected','Cancelled','Error')");
                    table.ForeignKey(
                        name: "FK_Payments_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventKey = table.Column<string>(type: "char(64)", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReceipts_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_PaymentId",
                table: "Movements",
                column: "PaymentId",
                unique: true,
                filter: "[PaymentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements",
                sql: "([PaymentId] IS NULL AND [DevelopmentFundingId] IS NOT NULL AND [TransferId] IS NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit') OR ([PaymentId] IS NULL AND [DevelopmentFundingId] IS NULL AND [TransferId] IS NOT NULL AND [Source] = 'Transfer' AND [Type] IN ('Debit', 'Credit')) OR ([PaymentId] IS NOT NULL AND [DevelopmentFundingId] IS NULL AND [TransferId] IS NULL AND [Source] = 'Payment' AND [Type] = 'Credit')");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipts_EventKey",
                table: "PaymentReceipts",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipts_PaymentId",
                table: "PaymentReceipts",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PreferenceId",
                table: "Payments",
                column: "PreferenceId",
                unique: true,
                filter: "[PreferenceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ProviderPaymentId",
                table: "Payments",
                columns: new[] { "Provider", "ProviderPaymentId" },
                unique: true,
                filter: "[ProviderPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_NextCheckAt",
                table: "Payments",
                columns: new[] { "Status", "NextCheckAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_WalletId_IdempotencyKey",
                table: "Payments",
                columns: new[] { "WalletId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_Payments_PaymentId",
                table: "Movements",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No elimino evidencia de operaciones financieras mediante una reversión de esquema.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Payments]) THROW 51000, 'Payments contiene operaciones. Se requiere un procedimiento de recuperación explícito.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_Payments_PaymentId",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "PaymentReceipts");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Movements_PaymentId",
                table: "Movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "Movements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Source",
                table: "Movements",
                sql: "([DevelopmentFundingId] IS NOT NULL AND [TransferId] IS NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit') OR ([DevelopmentFundingId] IS NULL AND [TransferId] IS NOT NULL AND [Source] = 'Transfer' AND [Type] IN ('Debit', 'Credit'))");
        }
    }
}
