using AudioYotoShelf.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioYotoShelf.Infrastructure.Data.Configurations;

public class UserConnectionConfiguration : IEntityTypeConfiguration<UserConnection>
{
    public void Configure(EntityTypeBuilder<UserConnection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Username).IsUnique();

        builder.Property(x => x.Username).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AudiobookshelfUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.AudiobookshelfToken).HasMaxLength(4096);
        builder.Property(x => x.YotoAccessToken).HasMaxLength(4096);
        builder.Property(x => x.YotoRefreshToken).HasMaxLength(4096);
        builder.Property(x => x.YotoDeviceCode).HasMaxLength(512);
        builder.Property(x => x.DefaultLibraryId).HasMaxLength(256);
    }
}

public class CardTransferConfiguration : IEntityTypeConfiguration<CardTransfer>
{
    public void Configure(EntityTypeBuilder<CardTransfer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.AbsLibraryItemId);
        builder.HasIndex(x => x.YotoCardId);
        builder.HasIndex(x => new { x.UserConnectionId, x.Status });

        builder.Property(x => x.AbsLibraryItemId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.BookTitle).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.BookAuthor).HasMaxLength(1024);
        builder.Property(x => x.SeriesName).HasMaxLength(1024);
        builder.Property(x => x.YotoCardId).HasMaxLength(256);
        builder.Property(x => x.AgeSuggestionReason).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(4096);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.PlaybackType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.AgeSuggestionSource).HasConversion<string>().HasMaxLength(64);

        builder.HasOne(x => x.UserConnection)
            .WithMany(x => x.CardTransfers)
            .HasForeignKey(x => x.UserConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TrackMappingConfiguration : IEntityTypeConfiguration<TrackMapping>
{
    public void Configure(EntityTypeBuilder<TrackMapping> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.YotoTranscodedSha256);

        builder.Property(x => x.AbsFileIno).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ChapterTitle).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.YotoUploadId).HasMaxLength(256);
        builder.Property(x => x.YotoTranscodedSha256).HasMaxLength(256);
        builder.Property(x => x.YotoTrackUrl).HasMaxLength(2048);

        builder.HasOne(x => x.CardTransfer)
            .WithMany(x => x.TrackMappings)
            .HasForeignKey(x => x.CardTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.GeneratedIcon)
            .WithMany()
            .HasForeignKey(x => x.GeneratedIconId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class GeneratedIconConfiguration : IEntityTypeConfiguration<GeneratedIcon>
{
    public void Configure(EntityTypeBuilder<GeneratedIcon> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ContentHash);

        builder.Property(x => x.Prompt).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ContextTitle).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.YotoMediaId).HasMaxLength(256);
        builder.Property(x => x.YotoIconUrl).HasMaxLength(2048);
        builder.Property(x => x.PublicIconId).HasMaxLength(256);
        builder.Property(x => x.ContentHash).HasMaxLength(128);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(64);

        builder.HasOne(x => x.UserConnection)
            .WithMany(x => x.GeneratedIcons)
            .HasForeignKey(x => x.UserConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
