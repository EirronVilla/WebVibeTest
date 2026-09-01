using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayableGameState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoardSeed",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoardStateJson",
                table: "Games",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentPlayerUserId",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingSettlementVertexId",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Phase",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_CurrentPlayerUserId",
                table: "Games",
                column: "CurrentPlayerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Games_AspNetUsers_CurrentPlayerUserId",
                table: "Games",
                column: "CurrentPlayerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_AspNetUsers_CurrentPlayerUserId",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_CurrentPlayerUserId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BoardSeed",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BoardStateJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "CurrentPlayerUserId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PendingSettlementVertexId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "Games");
        }
    }
}
