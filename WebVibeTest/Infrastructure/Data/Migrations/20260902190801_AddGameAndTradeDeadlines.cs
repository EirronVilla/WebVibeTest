using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameAndTradeDeadlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseDeadlineUtc",
                table: "TradeOffers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ActionDeadlineUtc",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseDeadlineUtc",
                table: "TradeOffers");

            migrationBuilder.DropColumn(
                name: "ActionDeadlineUtc",
                table: "Games");
        }
    }
}
