using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationReadyAndNullableScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TechnicalEvaluations_BidId",
                table: "TechnicalEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_FinancialEvaluations_BidId",
                table: "FinancialEvaluations");

            migrationBuilder.AlterColumn<decimal>(
                name: "Score",
                table: "TechnicalEvaluations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Score",
                table: "FinancialEvaluations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyAt",
                table: "FinancialEvaluations",
                type: "datetime2",
                nullable: true);

            // The unique indexes below need one row per member (and criterion) and offer. If a development database
            // already holds a duplicate (for example from a double click), keep the most recently saved row so the
            // index can be created. On a clean database these two statements delete nothing.
            migrationBuilder.Sql(@"
DELETE t FROM TechnicalEvaluations t
WHERE EXISTS (SELECT 1 FROM TechnicalEvaluations o
              WHERE o.BidId = t.BidId AND o.CriterionId = t.CriterionId AND o.EvaluatorUserId = t.EvaluatorUserId
                AND (o.EvaluatedAt > t.EvaluatedAt OR (o.EvaluatedAt = t.EvaluatedAt AND o.Id > t.Id)));");
            migrationBuilder.Sql(@"
DELETE f FROM FinancialEvaluations f
WHERE EXISTS (SELECT 1 FROM FinancialEvaluations o
              WHERE o.BidId = f.BidId AND o.EvaluatorUserId = f.EvaluatorUserId
                AND (o.EvaluatedAt > f.EvaluatedAt OR (o.EvaluatedAt = f.EvaluatedAt AND o.Id > f.Id)));");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalEvaluations_BidId_CriterionId_EvaluatorUserId",
                table: "TechnicalEvaluations",
                columns: new[] { "BidId", "CriterionId", "EvaluatorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvaluations_BidId_EvaluatorUserId",
                table: "FinancialEvaluations",
                columns: new[] { "BidId", "EvaluatorUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TechnicalEvaluations_BidId_CriterionId_EvaluatorUserId",
                table: "TechnicalEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_FinancialEvaluations_BidId_EvaluatorUserId",
                table: "FinancialEvaluations");

            migrationBuilder.DropColumn(
                name: "ReadyAt",
                table: "FinancialEvaluations");

            migrationBuilder.AlterColumn<decimal>(
                name: "Score",
                table: "TechnicalEvaluations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Score",
                table: "FinancialEvaluations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalEvaluations_BidId",
                table: "TechnicalEvaluations",
                column: "BidId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvaluations_BidId",
                table: "FinancialEvaluations",
                column: "BidId");
        }
    }
}
