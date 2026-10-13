using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VortexTrading.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMinuteBars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "minute_bars",
                schema: "trading",
                columns: table => new
                {
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Open = table.Column<decimal>(type: "numeric", nullable: false),
                    High = table.Column<decimal>(type: "numeric", nullable: false),
                    Low = table.Column<decimal>(type: "numeric", nullable: false),
                    Close = table.Column<decimal>(type: "numeric", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_minute_bars", x => new { x.InstrumentId, x.OpenTime });
                });

            migrationBuilder.CreateIndex(
                name: "IX_minute_bars_OpenTime",
                schema: "trading",
                table: "minute_bars",
                column: "OpenTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "minute_bars",
                schema: "trading");
        }
    }
}
