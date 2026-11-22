using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class AssistantQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Feedback",
                table: "ChatMessages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GoldenQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Question = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExpectedDocument = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MustContain = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastPassed = table.Column<bool>(type: "bit", nullable: true),
                    LastDetail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldenQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoldenQuestions_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoldenQuestions_CreatedById",
                table: "GoldenQuestions",
                column: "CreatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoldenQuestions");

            migrationBuilder.DropColumn(
                name: "Feedback",
                table: "ChatMessages");
        }
    }
}
