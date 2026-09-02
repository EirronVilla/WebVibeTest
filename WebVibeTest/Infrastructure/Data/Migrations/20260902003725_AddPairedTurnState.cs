using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPairedTurnState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSecondaryActionPhase",
                table: "Games",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryPlayerUserId",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryPlayerUserId",
                table: "Games",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSecondaryActionPhase",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PrimaryPlayerUserId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SecondaryPlayerUserId",
                table: "Games");
        }
    }
}
