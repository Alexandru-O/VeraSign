using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletEnrollmentPidEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PidEmail",
                table: "WalletEnrollments",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletEnrollments_PidEmail",
                table: "WalletEnrollments",
                column: "PidEmail",
                filter: "[PidEmail] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletEnrollments_PidEmail",
                table: "WalletEnrollments");

            migrationBuilder.DropColumn(
                name: "PidEmail",
                table: "WalletEnrollments");
        }
    }
}
