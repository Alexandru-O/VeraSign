using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSigningRequestPreparedFieldName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreparedFieldName",
                table: "SigningRequests",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreparedFieldName",
                table: "SigningRequests");
        }
    }
}
