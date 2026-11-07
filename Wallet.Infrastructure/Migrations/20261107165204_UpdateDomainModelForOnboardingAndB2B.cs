using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDomainModelForOnboardingAndB2B : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "Wallets",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Cvu",
                table: "Wallets",
                type: "nvarchar(22)",
                maxLength: 22,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Dni",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "WalletId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cuit",
                table: "ExternalClients",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecret",
                table: "ExternalClients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "ExternalClients",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            // Populate identifiers before adding unique indexes on existing wallets.
            migrationBuilder.Sql("EXEC(N'" + HostedUpgradeDataSql.WalletIdentifiers.Replace("'", "''") + "');");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Alias",
                table: "Wallets",
                column: "Alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Cvu",
                table: "Wallets",
                column: "Cvu",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalClients_Cuit",
                table: "ExternalClients",
                column: "Cuit",
                unique: true,
                filter: "[Cuit] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wallets_Alias",
                table: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_Wallets_Cvu",
                table: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_ExternalClients_Cuit",
                table: "ExternalClients");

            migrationBuilder.DropColumn(
                name: "Alias",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "Cvu",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "Dni",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WalletId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Cuit",
                table: "ExternalClients");

            migrationBuilder.DropColumn(
                name: "WebhookSecret",
                table: "ExternalClients");

            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "ExternalClients");
        }
    }
}
