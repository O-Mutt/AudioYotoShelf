using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioYotoShelf.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AudiobookshelfUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AudiobookshelfToken = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    AudiobookshelfTokenValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    YotoAccessToken = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    YotoRefreshToken = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    YotoTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    YotoDeviceCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DefaultLibraryId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DefaultMinAge = table.Column<int>(type: "integer", nullable: false),
                    DefaultMaxAge = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsLibraryItemId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BookTitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    BookAuthor = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SeriesName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SeriesSequence = table.Column<float>(type: "real", nullable: true),
                    YotoCardId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlaybackType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuggestedMinAge = table.Column<int>(type: "integer", nullable: false),
                    SuggestedMaxAge = table.Column<int>(type: "integer", nullable: false),
                    AgeSuggestionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AgeSuggestionSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OverrideMinAge = table.Column<int>(type: "integer", nullable: true),
                    OverrideMaxAge = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardTransfers_UserConnections_UserConnectionId",
                        column: x => x.UserConnectionId,
                        principalTable: "UserConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedIcons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prompt = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ContextTitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsBookCover = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    YotoMediaId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    YotoIconUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PublicIconId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IconData = table.Column<byte[]>(type: "bytea", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TimesUsed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedIcons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedIcons_UserConnections_UserConnectionId",
                        column: x => x.UserConnectionId,
                        principalTable: "UserConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsFileIno = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChapterTitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ChapterIndex = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<double>(type: "double precision", nullable: false),
                    EndTime = table.Column<double>(type: "double precision", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    YotoUploadId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    YotoTranscodedSha256 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    YotoTrackUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TranscodedDuration = table.Column<double>(type: "double precision", nullable: true),
                    TranscodedFileSize = table.Column<long>(type: "bigint", nullable: true),
                    GeneratedIconId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackMappings_CardTransfers_CardTransferId",
                        column: x => x.CardTransferId,
                        principalTable: "CardTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackMappings_GeneratedIcons_GeneratedIconId",
                        column: x => x.GeneratedIconId,
                        principalTable: "GeneratedIcons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardTransfers_AbsLibraryItemId",
                table: "CardTransfers",
                column: "AbsLibraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CardTransfers_UserConnectionId_Status",
                table: "CardTransfers",
                columns: new[] { "UserConnectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CardTransfers_YotoCardId",
                table: "CardTransfers",
                column: "YotoCardId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedIcons_ContentHash",
                table: "GeneratedIcons",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedIcons_UserConnectionId",
                table: "GeneratedIcons",
                column: "UserConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackMappings_CardTransferId",
                table: "TrackMappings",
                column: "CardTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackMappings_GeneratedIconId",
                table: "TrackMappings",
                column: "GeneratedIconId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackMappings_YotoTranscodedSha256",
                table: "TrackMappings",
                column: "YotoTranscodedSha256");

            migrationBuilder.CreateIndex(
                name: "IX_UserConnections_Username",
                table: "UserConnections",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackMappings");

            migrationBuilder.DropTable(
                name: "CardTransfers");

            migrationBuilder.DropTable(
                name: "GeneratedIcons");

            migrationBuilder.DropTable(
                name: "UserConnections");
        }
    }
}
