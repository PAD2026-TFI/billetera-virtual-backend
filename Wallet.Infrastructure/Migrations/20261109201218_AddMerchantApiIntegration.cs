using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantApiIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "personal");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IntegrationCreatedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrationKeyHash",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrationKeyPrefix",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrationName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IntegrationRevokedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IntegrationRotatedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntegrationVersion",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Users_IntegrationKeyHash",
                table: "Users",
                column: "IntegrationKeyHash",
                unique: true,
                filter: "[IntegrationKeyHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IntegrationVersion", table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_IntegrationKeyHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IntegrationCreatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IntegrationKeyHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IntegrationKeyPrefix",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IntegrationName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IntegrationRevokedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IntegrationRotatedAt",
                table: "Users");
        }
    }
}
