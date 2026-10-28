using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchApiCredentialGrnApiResponse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "GET"),
                    AuthType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HeaderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SecretEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExtraHeadersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RequestBodyTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseRootPath = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlantCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiFieldMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiCredentialId = table.Column<int>(type: "int", nullable: false),
                    SourceField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetColumn = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefaultValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiFieldMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiFieldMappings_ApiCredentials_ApiCredentialId",
                        column: x => x.ApiCredentialId,
                        principalTable: "ApiCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrnApiResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiCredentialId = table.Column<int>(type: "int", nullable: false),
                    FetchBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FetchedBy = table.Column<int>(type: "int", nullable: true),
                    GrnNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GrnYear = table.Column<int>(type: "int", nullable: true),
                    GrnItemNo = table.Column<int>(type: "int", nullable: true),
                    GrnDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvoiceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Uom = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    MaterialCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PartName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Plant = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    StorageLocation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PoNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PoItem = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    VendorCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VendorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrnApiResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrnApiResponses_ApiCredentials_ApiCredentialId",
                        column: x => x.ApiCredentialId,
                        principalTable: "ApiCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "IsActive", "ModifiedAt", "ModifiedBy", "Name", "PlantCode" },
                values: new object[] { 1, "MAIN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1, true, null, null, "Main", null });

            migrationBuilder.CreateIndex(
                name: "IX_ApiFieldMappings_ApiCredentialId_TargetColumn",
                table: "ApiFieldMappings",
                columns: new[] { "ApiCredentialId", "TargetColumn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrnApiResponses_ApiCredentialId",
                table: "GrnApiResponses",
                column: "ApiCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_GrnApiResponses_BranchId_GrnNo",
                table: "GrnApiResponses",
                columns: new[] { "BranchId", "GrnNo" });

            migrationBuilder.CreateIndex(
                name: "IX_GrnApiResponses_BranchId_SourceKey",
                table: "GrnApiResponses",
                columns: new[] { "BranchId", "SourceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_GrnApiResponses_BranchId_Status",
                table: "GrnApiResponses",
                columns: new[] { "BranchId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiFieldMappings");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "GrnApiResponses");

            migrationBuilder.DropTable(
                name: "ApiCredentials");
        }
    }
}
