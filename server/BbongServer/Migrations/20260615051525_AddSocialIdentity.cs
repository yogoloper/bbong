using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BbongServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "accounts");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_Provider_SocialSubject",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "SocialSubject",
                table: "accounts");

            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
