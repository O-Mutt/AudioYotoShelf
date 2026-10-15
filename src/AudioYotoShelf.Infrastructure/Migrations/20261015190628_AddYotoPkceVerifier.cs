using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioYotoShelf.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddYotoPkceVerifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "YotoPkceVerifier",
                table: "UserConnections",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YotoPkceVerifier",
                table: "UserConnections");
        }
    }
}
