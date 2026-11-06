using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WalutyKursNbp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Invoices",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateDate",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateTable",
                table: "Invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateTable",
                table: "Invoices");
        }
    }
}
