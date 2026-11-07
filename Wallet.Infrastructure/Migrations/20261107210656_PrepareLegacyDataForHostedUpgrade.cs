using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrepareLegacyDataForHostedUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("EXEC(N'" + HostedUpgradeDataSql.WalletIdentifiers.Replace("'", "''") + "');");
            migrationBuilder.Sql("EXEC(N'" + HostedUpgradeDataSql.CardRequestTypes.Replace("'", "''") + "');");

            migrationBuilder.DropIndex(
                name: "IX_ExternalClients_Cuit",
                table: "ExternalClients");

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
                name: "IX_ExternalClients_Cuit",
                table: "ExternalClients");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalClients_Cuit",
                table: "ExternalClients",
                column: "Cuit",
                unique: true,
                filter: "[Cuit] IS NOT NULL");
        }
    }
}
