using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSignedDocumentPageManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PageManifestJson",
                table: "SignedDocuments",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageManifestJson",
                table: "SignedDocuments");
        }
    }
}
