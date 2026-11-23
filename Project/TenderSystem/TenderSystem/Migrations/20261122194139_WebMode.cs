using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class WebMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebAllowedDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Domain = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAllowedDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebAllowedDomains_AspNetUsers_AddedById",
                        column: x => x.AddedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebAllowedDomains_AddedById",
                table: "WebAllowedDomains",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_WebAllowedDomains_Domain",
                table: "WebAllowedDomains",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebAllowedDomains");
        }
    }
}
