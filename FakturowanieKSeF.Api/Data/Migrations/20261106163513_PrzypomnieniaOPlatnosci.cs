using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrzypomnieniaOPlatnosci : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRemindersEnabled",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReminderDaysAfterDue",
                table: "Companies",
                type: "integer",
                nullable: false,
                defaultValue: 3); // istniejące firmy: domyślnie 3 dni po terminie (jak w modelu)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoRemindersEnabled",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ReminderDaysAfterDue",
                table: "Companies");
        }
    }
}
