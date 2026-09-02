using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCompletionResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WinnerUserId",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CitiesBuilt",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DevelopmentCardsBought",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DevelopmentCardsPlayed",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FinalRank",
                table: "GamePlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinalVictoryPoints",
                table: "GamePlayers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWinner",
                table: "GamePlayers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RoadsBuilt",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SettlementsBuilt",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalResourcesGained",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WinnerUserId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "CitiesBuilt",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "DevelopmentCardsBought",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "DevelopmentCardsPlayed",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "FinalRank",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "FinalVictoryPoints",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "IsWinner",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "RoadsBuilt",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "SettlementsBuilt",
                table: "GamePlayers");

            migrationBuilder.DropColumn(
                name: "TotalResourcesGained",
                table: "GamePlayers");
        }
    }
}
