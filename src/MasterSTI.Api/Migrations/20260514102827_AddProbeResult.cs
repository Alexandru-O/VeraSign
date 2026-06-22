using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterSTI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProbeResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProbeResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Node = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Health = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    RttMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProbeResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProbeResults_Node_Timestamp",
                table: "ProbeResults",
                columns: new[] { "Node", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ProbeResults_Timestamp",
                table: "ProbeResults",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProbeResults");
        }
    }
}
