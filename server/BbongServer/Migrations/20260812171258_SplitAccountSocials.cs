using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BbongServer.Migrations
{
    /// <inheritdoc />
    public partial class SplitAccountSocials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_Provider_SocialSubject",
                table: "accounts");

            migrationBuilder.CreateTable(
                name: "account_socials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_socials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_socials_AccountId",
                table: "account_socials",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_account_socials_AccountId_Provider",
                table: "account_socials",
                columns: new[] { "AccountId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_socials_Provider_Subject",
                table: "account_socials",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            // 기존 컬럼에 담겨 있던 연동을 새 테이블로 옮긴다.
            // 운영 기준 0건이지만, 로컬·테스트 데이터가 조용히 사라지면 안 된다.
            migrationBuilder.Sql(@"
                INSERT INTO account_socials (""AccountId"", ""Provider"", ""Subject"")
                SELECT ""Id"", ""Provider"", ""SocialSubject""
                FROM accounts
                WHERE ""Provider"" IS NOT NULL AND ""SocialSubject"" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "SocialSubject",
                table: "accounts");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_socials");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialSubject",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Provider_SocialSubject",
                table: "accounts",
                columns: new[] { "Provider", "SocialSubject" },
                unique: true,
                filter: "\"Provider\" IS NOT NULL");
        }
    }
}
