using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdentityVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IdentityVerifiedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Dni",
                table: "Users",
                column: "Dni",
                unique: true,
                filter: "[Dni] <> '00000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Dni",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IdentityVerifiedAt",
                table: "Users");
        }
    }
}
