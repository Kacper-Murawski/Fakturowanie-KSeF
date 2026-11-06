using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FakturyCykliczne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TemplateInvoiceId = table.Column<int>(type: "integer", nullable: false),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    NextIssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyByEmail = table.Column<bool>(type: "boolean", nullable: false),
                    IssuedCount = table.Column<int>(type: "integer", nullable: false),
                    LastIssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastInvoiceId = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringInvoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringInvoices_Invoices_TemplateInvoiceId",
                        column: x => x.TemplateInvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_CompanyId",
                table: "RecurringInvoices",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_IsActive_NextIssueDate",
                table: "RecurringInvoices",
                columns: new[] { "IsActive", "NextIssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_TemplateInvoiceId",
                table: "RecurringInvoices",
                column: "TemplateInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringInvoices");
        }
    }
}
