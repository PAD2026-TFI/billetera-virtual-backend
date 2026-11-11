using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordResetChallenges",
                columns: table => new
                {
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CodeExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResendAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptsLeft = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetChallenges", x => x.Email);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetChallenges");
        }
    }
}
