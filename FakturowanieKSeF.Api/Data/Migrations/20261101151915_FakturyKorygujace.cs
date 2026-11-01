using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FakturyKorygujace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CorrectedInvoiceId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CorrectedInvoiceIssueDate",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectedInvoiceKsefNumber",
                table: "Invoices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectedInvoiceNumber",
                table: "Invoices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "Invoices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionType",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Vat"); // istniejące faktury są fakturami VAT

            migrationBuilder.AddColumn<bool>(
                name: "BeforeCorrection",
                table: "InvoiceItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CorrectedInvoiceId",
                table: "Invoices",
                column: "CorrectedInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Invoices_CorrectedInvoiceId",
                table: "Invoices",
                column: "CorrectedInvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Invoices_CorrectedInvoiceId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_CorrectedInvoiceId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectedInvoiceId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectedInvoiceIssueDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectedInvoiceKsefNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectedInvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CorrectionType",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BeforeCorrection",
                table: "InvoiceItems");
        }
    }
}
