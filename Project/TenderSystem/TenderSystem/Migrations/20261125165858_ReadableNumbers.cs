using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSystem.Migrations
{
    /// <inheritdoc />
    public partial class ReadableNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Old tenders and requests got a random number (TND-51a7a2859f4d4bc5910f3dba, PR-20261124121136-0ed2385b). New ones get TND-2026-0030 / PR-2026-0028,
            // so the old random ones are renamed the same way (year of creation + id). Only numbers that match the old random pattern are touched, the
            // demo numbers (TND-DEMO-01, LAB-01-FLOW...) stay. Text that was saved earlier (notifications, audit details) keeps the old number.
            migrationBuilder.Sql(@"
UPDATE Tenders SET TenderNumber = 'TND-' + CAST(YEAR(CreatedAt) AS varchar(4)) + '-' + RIGHT('0000' + CAST(Id AS varchar(10)), 4)
WHERE LEN(TenderNumber) = 28 AND TenderNumber LIKE 'TND-%' AND SUBSTRING(TenderNumber, 5, 24) NOT LIKE '%[^0-9a-f]%';");
            migrationBuilder.Sql(@"
UPDATE PurchaseRequisitions SET RequestNumber = 'PR-' + CAST(YEAR(CreatedAt) AS varchar(4)) + '-' + RIGHT('0000' + CAST(Id AS varchar(10)), 4)
WHERE LEN(RequestNumber) = 26 AND RequestNumber LIKE 'PR-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]-%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old random numbers are not kept, so they cannot be restored. The short numbers are valid on their own.
        }
    }
}
