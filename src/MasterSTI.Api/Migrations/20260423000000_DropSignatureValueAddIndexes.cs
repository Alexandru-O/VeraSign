using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations;

public partial class DropSignatureValueAddIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SignatureValue",
            table: "SigningRequests");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_UploadedAt",
            table: "Documents",
            column: "UploadedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Documents_UploadedAt",
            table: "Documents");

        migrationBuilder.AddColumn<string>(
            name: "SignatureValue",
            table: "SigningRequests",
            type: "nvarchar(max)",
            nullable: true);
    }
}
