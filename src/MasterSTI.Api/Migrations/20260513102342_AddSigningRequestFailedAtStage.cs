using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSigningRequestFailedAtStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedAtStage",
                table: "SigningRequests",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedAtStage",
                table: "SigningRequests");
        }
    }
}
