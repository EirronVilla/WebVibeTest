using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoadAndArmyAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LargestArmyHolderUserId",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LongestRoadHolderUserId",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LongestRoadLength",
                table: "Games",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LargestArmyHolderUserId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LongestRoadHolderUserId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LongestRoadLength",
                table: "Games");
        }
    }
}
