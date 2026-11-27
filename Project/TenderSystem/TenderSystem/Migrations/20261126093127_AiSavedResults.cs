using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class AiSavedResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiSavedResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSavedResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSavedResults_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiSavedResults_Kind_OwnerUserId_Key",
                table: "AiSavedResults",
                columns: new[] { "Kind", "OwnerUserId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSavedResults_OwnerUserId",
                table: "AiSavedResults",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSavedResults_Token",
                table: "AiSavedResults",
                column: "Token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSavedResults");
        }
    }
}
