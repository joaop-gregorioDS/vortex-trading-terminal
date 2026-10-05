using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VortexTrading.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTradingOrderAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    LimitPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    TimeInForce = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "order_fills",
                schema: "trading",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_fills", x => x.ExecutionId);
                    table.ForeignKey(
                        name: "FK_order_fills_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "trading",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_fills_OrderId",
                schema: "trading",
                table: "order_fills",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_InstrumentId",
                schema: "trading",
                table: "orders",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status",
                schema: "trading",
                table: "orders",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_fills",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "trading");
        }
    }
}
