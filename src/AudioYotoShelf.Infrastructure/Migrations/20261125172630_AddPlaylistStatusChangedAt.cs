using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioYotoShelf.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistStatusChangedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusChangedAt",
                table: "Playlists",
                type: "timestamp with time zone",
                nullable: true);

            // Best available guess for cards sent before the column existed.
            migrationBuilder.Sql(
                """UPDATE "Playlists" SET "StatusChangedAt" = COALESCE("TransferredAt", "UpdatedAt") WHERE "Status" <> 'Draft';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "Playlists");
        }
    }
}
