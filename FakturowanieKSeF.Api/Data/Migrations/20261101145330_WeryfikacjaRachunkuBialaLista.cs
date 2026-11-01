using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WeryfikacjaRachunkuBialaLista : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountCheckRequestId",
                table: "ReceivedInvoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BankAccountCheckedOn",
                table: "ReceivedInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BankAccountOnWhiteList",
                table: "ReceivedInvoices",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountCheckRequestId",
                table: "ReceivedInvoices");

            migrationBuilder.DropColumn(
                name: "BankAccountCheckedOn",
                table: "ReceivedInvoices");

            migrationBuilder.DropColumn(
                name: "BankAccountOnWhiteList",
                table: "ReceivedInvoices");
        }
    }
}
