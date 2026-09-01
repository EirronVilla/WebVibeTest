using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebVibeTest.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisibleVictoryPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VisibleVictoryPoints",
                table: "GamePlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "GamePlayers" AS player
                SET "VisibleVictoryPoints" = COALESCE((
                    SELECT SUM(
                        CASE
                            WHEN (vertex->'Settlement'->>'BuildingType')::integer = 1 THEN 2
                            ELSE 1
                        END)::integer
                    FROM jsonb_array_elements(game."BoardStateJson"->'Vertices') AS vertex
                    WHERE vertex->'Settlement' IS NOT NULL
                      AND vertex->'Settlement' <> 'null'::jsonb
                      AND vertex->'Settlement'->>'UserId' = player."UserId"
                ), 0)
                FROM "Games" AS game
                WHERE player."GameId" = game."Id"
                  AND game."BoardStateJson" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisibleVictoryPoints",
                table: "GamePlayers");
        }
    }
}
