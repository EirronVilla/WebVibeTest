using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDevelopmentCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DevelopmentCardPlayedThisTurn",
                table: "Games",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DevelopmentDeckJson",
                table: "Games",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FreeRoadsRemaining",
                table: "Games",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResourceBankJson",
                table: "Games",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TurnNumber",
                table: "Games",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "KnightsPlayed",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DevelopmentCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PurchasedTurnNumber = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevelopmentCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DevelopmentCards_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DevelopmentCards_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DevelopmentCards_GameId_OwnerUserId",
                table: "DevelopmentCards",
                columns: new[] { "GameId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DevelopmentCards_OwnerUserId",
                table: "DevelopmentCards",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevelopmentCards");

            migrationBuilder.DropColumn(
                name: "DevelopmentCardPlayedThisTurn",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DevelopmentDeckJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FreeRoadsRemaining",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ResourceBankJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TurnNumber",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "KnightsPlayed",
                table: "GamePlayers");
        }
    }
}
