using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationCommitteeSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "FinancialEvaluations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvaluationCommitteeSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    EvaluatorUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReadyAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VotingRound = table.Column<int>(type: "int", nullable: false),
                    RecommendedBidId = table.Column<int>(type: "int", nullable: true),
                    VotedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCommitteeSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationCommitteeSubmissions_AspNetUsers_EvaluatorUserId",
                        column: x => x.EvaluatorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationCommitteeSubmissions_Bids_RecommendedBidId",
                        column: x => x.RecommendedBidId,
                        principalTable: "Bids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationCommitteeSubmissions_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCommitteeSubmissions_EvaluatorUserId",
                table: "EvaluationCommitteeSubmissions",
                column: "EvaluatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCommitteeSubmissions_RecommendedBidId",
                table: "EvaluationCommitteeSubmissions",
                column: "RecommendedBidId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCommitteeSubmissions_TenderId_EvaluatorUserId",
                table: "EvaluationCommitteeSubmissions",
                columns: new[] { "TenderId", "EvaluatorUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationCommitteeSubmissions");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "FinancialEvaluations");
        }
    }
}
