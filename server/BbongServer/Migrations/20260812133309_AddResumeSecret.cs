using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BbongServer.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResumeSecretHash",
                table: "accounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResumeSecretHash",
                table: "accounts");
        }
    }
}
