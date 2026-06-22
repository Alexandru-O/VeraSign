using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Sha256Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SigningRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CredentialId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SignatureLevel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreparedStoragePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SignatureValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EudiwSubject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SigningRequests_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SignedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SigningRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PadesLevel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TimestampToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidationReport = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignedDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignedDocuments_Documents_OriginalDocumentId",
                        column: x => x.OriginalDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SignedDocuments_SigningRequests_SigningRequestId",
                        column: x => x.SigningRequestId,
                        principalTable: "SigningRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocuments_OriginalDocumentId",
                table: "SignedDocuments",
                column: "OriginalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocuments_SigningRequestId",
                table: "SignedDocuments",
                column: "SigningRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequests_DocumentId",
                table: "SigningRequests",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignedDocuments");

            migrationBuilder.DropTable(
                name: "SigningRequests");

            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
