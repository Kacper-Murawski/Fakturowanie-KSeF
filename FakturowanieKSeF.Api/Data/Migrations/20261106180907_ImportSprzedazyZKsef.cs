using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportSprzedazyZKsef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_CompanyId_Number",
                table: "Invoices");

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "App");

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedInvoicesLastSyncAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuedInvoicesLastSyncError",
                table: "Companies",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedInvoicesSyncedUntil",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_KsefNumber",
                table: "Invoices",
                columns: new[] { "CompanyId", "KsefNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_Number",
                table: "Invoices",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Origin\" = 'App'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_CompanyId_KsefNumber",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_CompanyId_Number",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedInvoicesLastSyncAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IssuedInvoicesLastSyncError",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IssuedInvoicesSyncedUntil",
                table: "Companies");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_Number",
                table: "Invoices",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
