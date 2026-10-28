using System;
using Inventory.Infrastructure.Persistence.Scripts;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTagsAndPrintLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagCounters",
                columns: table => new
                {
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNo = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagCounters", x => new { x.BranchId, x.Year });
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrnApiResponseId = table.Column<int>(type: "int", nullable: false),
                    TagNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PrintCount = table.Column<int>(type: "int", nullable: false),
                    FirstPrintedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastPrintedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_GrnApiResponses_GrnApiResponseId",
                        column: x => x.GrnApiResponseId,
                        principalTable: "GrnApiResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TagPrintLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TagId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PrintedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagPrintLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagPrintLogs_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrnApiResponses_BranchId_Status_GrnDate",
                table: "GrnApiResponses",
                columns: new[] { "BranchId", "Status", "GrnDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TagPrintLogs_TagId",
                table: "TagPrintLogs",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagPrintLogs_UserId",
                table: "TagPrintLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_BranchId_TagNo",
                table: "Tags",
                columns: new[] { "BranchId", "TagNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_GrnApiResponseId",
                table: "Tags",
                column: "GrnApiResponseId",
                unique: true);

            migrationBuilder.Sql(StoredProcedureScripts.DropUspAllocateTagNumbers);
            migrationBuilder.Sql(StoredProcedureScripts.CreateUspAllocateTagNumbers);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StoredProcedureScripts.DropUspAllocateTagNumbers);

            migrationBuilder.DropTable(
                name: "TagCounters");

            migrationBuilder.DropTable(
                name: "TagPrintLogs");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_GrnApiResponses_BranchId_Status_GrnDate",
                table: "GrnApiResponses");
        }
    }
}
