using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StatusPlatnosci : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "ReceivedInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true),
                    ReceivedInvoiceId = table.Column<int>(type: "integer", nullable: true),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_OneInvoice", "(\"InvoiceId\" IS NULL) <> (\"ReceivedInvoiceId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Payments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_ReceivedInvoices_ReceivedInvoiceId",
                        column: x => x.ReceivedInvoiceId,
                        principalTable: "ReceivedInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CompanyId",
                table: "Payments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId",
                table: "Payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReceivedInvoiceId",
                table: "Payments",
                column: "ReceivedInvoiceId");

            // Uzupełnienie terminu płatności faktur pobranych przed tą zmianą - z XML (TerminPlatnosci/Termin),
            // niezależnie od prefiksu przestrzeni nazw
            migrationBuilder.Sql("""
                UPDATE "ReceivedInvoices"
                SET "DueDate" = substring("Xml" from '<(?:[A-Za-z0-9_]+:)?Termin>\s*([0-9]{4}-[0-9]{2}-[0-9]{2})\s*</')::date
                WHERE "DueDate" IS NULL AND "Xml" ~ '<(?:[A-Za-z0-9_]+:)?Termin>\s*[0-9]{4}-[0-9]{2}-[0-9]{2}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "ReceivedInvoices");
        }
    }
}
