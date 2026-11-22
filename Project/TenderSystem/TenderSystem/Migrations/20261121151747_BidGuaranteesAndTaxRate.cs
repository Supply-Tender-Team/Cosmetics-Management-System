using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class BidGuaranteesAndTaxRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "BidItems",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "BidGuaranteeId",
                table: "BidDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BidItemId",
                table: "BidDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BidGuarantees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    GuaranteeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IssuerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BeneficiaryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GuarantorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GuarantorNationalNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GuarantorAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    GuarantorPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VerificationCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidGuarantees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BidGuarantees_Bids_BidId",
                        column: x => x.BidId,
                        principalTable: "Bids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BidDocuments_BidGuaranteeId",
                table: "BidDocuments",
                column: "BidGuaranteeId");

            migrationBuilder.CreateIndex(
                name: "IX_BidDocuments_BidItemId",
                table: "BidDocuments",
                column: "BidItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BidGuarantees_BidId",
                table: "BidGuarantees",
                column: "BidId");

            migrationBuilder.CreateIndex(
                name: "IX_BidGuarantees_VerificationCode",
                table: "BidGuarantees",
                column: "VerificationCode",
                unique: true,
                filter: "[VerificationCode] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_BidDocuments_BidGuarantees_BidGuaranteeId",
                table: "BidDocuments",
                column: "BidGuaranteeId",
                principalTable: "BidGuarantees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BidDocuments_BidItems_BidItemId",
                table: "BidDocuments",
                column: "BidItemId",
                principalTable: "BidItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BidDocuments_BidGuarantees_BidGuaranteeId",
                table: "BidDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_BidDocuments_BidItems_BidItemId",
                table: "BidDocuments");

            migrationBuilder.DropTable(
                name: "BidGuarantees");

            migrationBuilder.DropIndex(
                name: "IX_BidDocuments_BidGuaranteeId",
                table: "BidDocuments");

            migrationBuilder.DropIndex(
                name: "IX_BidDocuments_BidItemId",
                table: "BidDocuments");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "BidItems");

            migrationBuilder.DropColumn(
                name: "BidGuaranteeId",
                table: "BidDocuments");

            migrationBuilder.DropColumn(
                name: "BidItemId",
                table: "BidDocuments");
        }
    }
}
