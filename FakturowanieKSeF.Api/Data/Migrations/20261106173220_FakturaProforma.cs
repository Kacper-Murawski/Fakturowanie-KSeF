using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FakturaProforma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProformaId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ProformaId",
                table: "Invoices",
                column: "ProformaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Invoices_ProformaId",
                table: "Invoices",
                column: "ProformaId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Invoices_ProformaId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ProformaId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ProformaId",
                table: "Invoices");
        }
    }
}
