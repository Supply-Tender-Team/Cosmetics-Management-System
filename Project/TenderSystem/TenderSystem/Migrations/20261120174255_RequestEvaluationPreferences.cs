using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class RequestEvaluationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvaluationFocus",
                table: "PurchaseRequisitions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinancialEvaluationWeight",
                table: "PurchaseRequisitions",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TechnicalEvaluationWeight",
                table: "PurchaseRequisitions",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequisitions_FinancialWeight",
                table: "PurchaseRequisitions",
                sql: "[FinancialEvaluationWeight] IS NULL OR ([FinancialEvaluationWeight] >= 0 AND [FinancialEvaluationWeight] <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequisitions_TechnicalWeight",
                table: "PurchaseRequisitions",
                sql: "[TechnicalEvaluationWeight] IS NULL OR ([TechnicalEvaluationWeight] >= 0 AND [TechnicalEvaluationWeight] <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequisitions_FinancialWeight",
                table: "PurchaseRequisitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequisitions_TechnicalWeight",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "EvaluationFocus",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "FinancialEvaluationWeight",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "TechnicalEvaluationWeight",
                table: "PurchaseRequisitions");
        }
    }
}
