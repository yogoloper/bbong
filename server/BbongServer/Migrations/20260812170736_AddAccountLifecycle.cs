using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BbongServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletionRequestedAt",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAt",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Status_DeletionRequestedAt",
                table: "accounts",
                columns: new[] { "Status", "DeletionRequestedAt" });
            migrationBuilder.Sql("UPDATE accounts SET \"Status\" = 'Active' WHERE \"Status\" = '' OR \"Status\" IS NULL;");

            // 마지막 접속은 기록된 적이 없다. 가입 시각으로 채워 정렬이 성립하게 하고,
            // 이후 로그인부터 실제 값이 쌓인다.
            migrationBuilder.Sql("UPDATE accounts SET \"LastLoginAt\" = \"CreatedAt\" WHERE \"LastLoginAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_Status_DeletionRequestedAt",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "accounts");
        }
    }
}
