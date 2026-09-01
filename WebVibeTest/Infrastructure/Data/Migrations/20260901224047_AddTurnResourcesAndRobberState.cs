using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnResourcesAndRobberState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LatestDie1",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LatestDie2",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Brick",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Grain",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Lumber",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Ore",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredDiscardCount",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Wool",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LatestDie1",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LatestDie2",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Brick",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "Grain",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "Lumber",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "Ore",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "RequiredDiscardCount",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "Wool",
                table: "GamePlayers");
        }
    }
}
