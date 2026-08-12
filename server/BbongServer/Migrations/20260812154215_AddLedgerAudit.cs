using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BbongServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BalanceAfter",
                table: "ledger",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ledger",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                table: "ledger",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "RefId",
                table: "ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefType",
                table: "ledger",
                type: "text",
                nullable: true);

            // 기존 행 백필. 지금은 837행이라 한 번에 끝나지만, 유저가 붙은 뒤엔 대량 UPDATE가 된다.
            // Kind: IAP 이전이라 과거 기록은 전부 무상 재화다.
            migrationBuilder.Sql("UPDATE ledger SET \"Kind\" = 'Free' WHERE \"Kind\" = '';");

            // OccurredAt: 실제 시각은 기록된 적이 없어 복원 불가다. 계정 생성 시각으로 채워
            // 최소한 정렬과 기간 필터가 성립하게 하고, 진짜 순서는 Id(시퀀스)가 계속 보증한다.
            migrationBuilder.Sql(@"
                UPDATE ledger l
                SET ""OccurredAt"" = a.""CreatedAt""
                FROM accounts a
                WHERE a.""Id"" = l.""UserId"" AND l.""OccurredAt"" = '0001-01-01 00:00:00+00';");

            // 계정이 사라졌거나 매칭되지 않는 잔여 행은 시각을 알 길이 없다.
            // 기본값(0001-01-01)을 그대로 두면 -infinity로 보이므로 epoch로 통일해 "미상"임을 분명히 한다.
            migrationBuilder.Sql(@"
                UPDATE ledger
                SET ""OccurredAt"" = '1970-01-01 00:00:00+00'
                WHERE ""OccurredAt"" < '1900-01-01 00:00:00+00';");

            // BalanceAfter: 유저별 Id 순 누적합. 지금은 윈도 함수 한 번이면 된다.
            migrationBuilder.Sql(@"
                UPDATE ledger l
                SET ""BalanceAfter"" = c.running
                FROM (
                    SELECT ""Id"", SUM(""Delta"") OVER (PARTITION BY ""UserId"" ORDER BY ""Id"") AS running
                    FROM ledger
                ) c
                WHERE c.""Id"" = l.""Id"";");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_RefType_RefId",
                table: "ledger",
                columns: new[] { "RefType", "RefId" },
                filter: "\"RefId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_UserId_OccurredAt",
                table: "ledger",
                columns: new[] { "UserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ledger_RefType_RefId",
                table: "ledger");

            migrationBuilder.DropIndex(
                name: "IX_ledger_UserId_OccurredAt",
                table: "ledger");

            migrationBuilder.DropColumn(
                name: "BalanceAfter",
                table: "ledger");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ledger");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "ledger");

            migrationBuilder.DropColumn(
                name: "RefId",
                table: "ledger");

            migrationBuilder.DropColumn(
                name: "RefType",
                table: "ledger");
        }
    }
}
