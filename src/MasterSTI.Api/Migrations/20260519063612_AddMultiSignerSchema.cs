using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiSignerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SignedDocuments_OriginalDocumentId",
                table: "SignedDocuments");

            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                table: "SigningRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientId",
                table: "SigningRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                table: "SignedDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousSignedDocumentId",
                table: "SignedDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientId",
                table: "SignedDocuments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequests_DocumentId_OrderIndex",
                table: "SigningRequests",
                columns: new[] { "DocumentId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequests_RecipientId",
                table: "SigningRequests",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocuments_OriginalDocumentId_IsFinal",
                table: "SignedDocuments",
                columns: new[] { "OriginalDocumentId", "IsFinal" });

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocuments_PreviousSignedDocumentId",
                table: "SignedDocuments",
                column: "PreviousSignedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocuments_RecipientId",
                table: "SignedDocuments",
                column: "RecipientId");

            migrationBuilder.AddForeignKey(
                name: "FK_SignedDocuments_Recipients_RecipientId",
                table: "SignedDocuments",
                column: "RecipientId",
                principalTable: "Recipients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SignedDocuments_SignedDocuments_PreviousSignedDocumentId",
                table: "SignedDocuments",
                column: "PreviousSignedDocumentId",
                principalTable: "SignedDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SigningRequests_Recipients_RecipientId",
                table: "SigningRequests",
                column: "RecipientId",
                principalTable: "Recipients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SignedDocuments_Recipients_RecipientId",
                table: "SignedDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_SignedDocuments_SignedDocuments_PreviousSignedDocumentId",
                table: "SignedDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_SigningRequests_Recipients_RecipientId",
                table: "SigningRequests");

            migrationBuilder.DropIndex(
                name: "IX_SigningRequests_DocumentId_OrderIndex",
                table: "SigningRequests");

            migrationBuilder.DropIndex(
                name: "IX_SigningRequests_RecipientId",
                table: "SigningRequests");

            migrationBuilder.DropIndex(
                name: "IX_SignedDocuments_OriginalDocumentId_IsFinal",
                table: "SignedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_SignedDocuments_PreviousSignedDocumentId",
                table: "SignedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_SignedDocuments_RecipientId",
                table: "SignedDocuments");

            migrationBuilder.DropColumn(
                name: "OrderIndex",
                table: "SigningRequests");

            migrationBuilder.DropColumn(
                name: "RecipientId",
                table: "SigningRequests");

            migrationBuilder.DropColumn(
                name: "IsFinal",
                table: "SignedDocuments");

            migrationBuilder.DropColumn(
                name: "PreviousSignedDocumentId",
                table: "SignedDocuments");

            migrationBuilder.DropColumn(
                name: "RecipientId",
                table: "SignedDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocuments_OriginalDocumentId",
                table: "SignedDocuments",
                column: "OriginalDocumentId");
        }
    }
}
