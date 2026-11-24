using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioYotoShelf.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCardSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CardSetId",
                table: "Playlists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SetIndex",
                table: "Playlists",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirstChapterIndex",
                table: "PlaylistItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastChapterIndex",
                table: "PlaylistItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartCount",
                table: "PlaylistItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartNumber",
                table: "PlaylistItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CardSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsSeriesId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AbsLibraryId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PackingMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExcludedItemIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardSets_UserConnections_UserConnectionId",
                        column: x => x.UserConnectionId,
                        principalTable: "UserConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_CardSetId_SetIndex",
                table: "Playlists",
                columns: new[] { "CardSetId", "SetIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_CardSets_UserConnectionId_AbsSeriesId",
                table: "CardSets",
                columns: new[] { "UserConnectionId", "AbsSeriesId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Playlists_CardSets_CardSetId",
                table: "Playlists",
                column: "CardSetId",
                principalTable: "CardSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Playlists_CardSets_CardSetId",
                table: "Playlists");

            migrationBuilder.DropTable(
                name: "CardSets");

            migrationBuilder.DropIndex(
                name: "IX_Playlists_CardSetId_SetIndex",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "CardSetId",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SetIndex",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "FirstChapterIndex",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "LastChapterIndex",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "PartCount",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "PartNumber",
                table: "PlaylistItems");
        }
    }
}
