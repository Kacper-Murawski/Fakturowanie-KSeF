using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakturowanieKSeF.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PotwierdzanieEmailILimityPoczty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "EmailMessages",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "EmailQuotas",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailQuotas", x => new { x.Key, x.Day });
                });

            // Konta istniejące przed wprowadzeniem wymogu potwierdzenia adresu (D53) są traktowane jako potwierdzone -
            // wymóg dotyczy tylko nowych rejestracji
            migrationBuilder.Sql("""UPDATE "AspNetUsers" SET "EmailConfirmed" = TRUE WHERE "EmailConfirmed" = FALSE;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailQuotas");

            // Wiadomości systemowe konta nie mają firmy - bez nich kolumna może znów być wymagana
            migrationBuilder.Sql("""DELETE FROM "EmailMessages" WHERE "CompanyId" IS NULL;""");

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "EmailMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
