using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OznaczenieZaplaty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FromInvoice",
                table: "Payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidOn",
                table: "Invoices",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromInvoice",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaidOn",
                table: "Invoices");
        }
    }
}
