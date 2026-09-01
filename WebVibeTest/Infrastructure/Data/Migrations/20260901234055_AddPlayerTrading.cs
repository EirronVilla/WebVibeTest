using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposerUserId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OfferedBrick = table.Column<int>(type: "integer", nullable: false),
                    OfferedLumber = table.Column<int>(type: "integer", nullable: false),
                    OfferedWool = table.Column<int>(type: "integer", nullable: false),
                    OfferedGrain = table.Column<int>(type: "integer", nullable: false),
                    OfferedOre = table.Column<int>(type: "integer", nullable: false),
                    RequestedBrick = table.Column<int>(type: "integer", nullable: false),
                    RequestedLumber = table.Column<int>(type: "integer", nullable: false),
                    RequestedWool = table.Column<int>(type: "integer", nullable: false),
                    RequestedGrain = table.Column<int>(type: "integer", nullable: false),
                    RequestedOre = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeOffers_AspNetUsers_ProposerUserId",
                        column: x => x.ProposerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradeOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeResponses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeResponses_TradeOffers_TradeOfferId",
                        column: x => x.TradeOfferId,
                        principalTable: "TradeOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_GameId_Status",
                table: "TradeOffers",
                columns: new[] { "GameId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_ProposerUserId",
                table: "TradeOffers",
                column: "ProposerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeResponses_TradeOfferId_UserId",
                table: "TradeResponses",
                columns: new[] { "TradeOfferId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeResponses_UserId",
                table: "TradeResponses",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeResponses");

            migrationBuilder.DropTable(
                name: "TradeOffers");
        }
    }
}
