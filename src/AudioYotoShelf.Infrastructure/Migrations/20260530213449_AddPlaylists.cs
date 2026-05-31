using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioYotoShelf.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefaultGrouping = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    YotoCardId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    TransferredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Playlists_UserConnections_UserConnectionId",
                        column: x => x.UserConnectionId,
                        principalTable: "UserConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsLibraryItemId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    BookTitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    BookAuthor = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SeriesName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SeriesSequence = table.Column<float>(type: "real", nullable: true),
                    GroupingOverride = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TrackDurations = table.Column<List<double>>(type: "double precision[]", nullable: false),
                    TrackBytes = table.Column<List<long>>(type: "bigint[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistId_Position",
                table: "PlaylistItems",
                columns: new[] { "PlaylistId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_UserConnectionId",
                table: "Playlists",
                column: "UserConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistItems");

            migrationBuilder.DropTable(
                name: "Playlists");
        }
    }
}
