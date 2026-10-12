using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VortexTrading.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "trading",
                table: "orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Session");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "trading",
                table: "orders");
        }
    }
}
