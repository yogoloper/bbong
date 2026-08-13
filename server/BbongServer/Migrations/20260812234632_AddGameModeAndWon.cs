using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BbongServer.Migrations
{
    /// <inheritdoc />
    public partial class AddGameModeAndWon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "games",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Won",
                table: "game_players",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 기존 기록 백필. TargetPlayers는 맞춤게임에서만 채워지므로 모드를 되짚을 수 있다.
            migrationBuilder.Sql(@"
                UPDATE games SET ""Mode"" =
                    CASE WHEN ""TargetPlayers"" > 0 THEN 'QuickMatch' ELSE 'Friend' END
                WHERE ""Mode"" = '' OR ""Mode"" IS NULL;");

            // 승패는 우승 좌석 CSV에 이미 들어 있어 그대로 되살릴 수 있다.
            migrationBuilder.Sql(@"
                UPDATE game_players p SET ""Won"" = true
                FROM games g
                WHERE g.""Id"" = p.""GameId""
                  AND g.""WinnerSeats"" IS NOT NULL
                  AND p.""Seat""::text = ANY(string_to_array(g.""WinnerSeats"", ','));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "games");

            migrationBuilder.DropColumn(
                name: "Won",
                table: "game_players");
        }
    }
}
