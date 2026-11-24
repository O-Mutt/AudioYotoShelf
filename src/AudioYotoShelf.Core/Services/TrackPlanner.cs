using AudioYotoShelf.Core.Configuration;
using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;

namespace AudioYotoShelf.Core.Services;

public class TrackPlanner(YotoCardLimits limits) : ITrackPlanner
{
    public IReadOnlyList<SourceTrack> Plan(AbsBookMedia media, TrackGrouping grouping = TrackGrouping.Chapters)
    {
        var chapters = PlanChapters(media);
        return ApplyGrouping(chapters, grouping, media.Metadata.Title ?? "Unknown");
    }

    public IReadOnlyList<SourceTrack> ApplyGrouping(
        IReadOnlyList<SourceTrack> chapterTracks, TrackGrouping grouping, string bookTitle)
    {
        if (chapterTracks.Count == 0) return chapterTracks;

        return grouping switch
        {
            TrackGrouping.SingleTrack => [Merge(chapterTracks, bookTitle)],
            TrackGrouping.Auto => FitsOneTrack(chapterTracks) ? [Merge(chapterTracks, bookTitle)] : chapterTracks,
            _ => chapterTracks
        };
    }

    private static IReadOnlyList<SourceTrack> PlanChapters(AbsBookMedia media)
    {
        var bookTitle = media.Metadata.Title ?? "Unknown";

        if (media.NumAudioFiles > 1)
        {
            // Multi-file book: each audio file is a track.
            return media.AudioFiles.OrderBy(f => f.Index).Select(f =>
            {
                var title = ChapterTitleResolver.ForFile(media.Chapters, f);
                return new SourceTrack(title, f.Duration, f.Size);
            }).ToList();
        }

        if (media.Chapters.Length > 0 && media.AudioFiles.Length == 1)
            return SplitFileByChapters(media.AudioFiles[0], media.Chapters, media.Duration);

        if (media.AudioFiles.Length == 1)
        {
            var f = media.AudioFiles[0];
            return [new SourceTrack(bookTitle, f.Duration, f.Size)];
        }

        return [];
    }

    private static IReadOnlyList<SourceTrack> SplitFileByChapters(
        AbsAudioFile file, AbsChapter[] chapters, double bookDuration)
    {
        // Chapter markers can carry timing before Audiobookshelf has scanned the file, so their
        // span is a better weight than an even split when neither the file nor the book has a duration.
        var totalDuration = file.Duration > 0 ? file.Duration
            : bookDuration > 0 ? bookDuration
            : chapters.Sum(c => c.End - c.Start);

        if (totalDuration > 0)
        {
            return chapters.Select(c =>
            {
                var duration = c.End - c.Start;
                return new SourceTrack(c.Title, duration, (long)(file.Size * (duration / totalDuration)));
            }).ToList();
        }

        // These chapters have no duration, so each one's bytes count in full as unscanned size;
        // the last takes the rounding remainder so the card total holds the file exactly once.
        var share = file.Size / chapters.Length;
        var lastShare = file.Size - share * (chapters.Length - 1);
        return chapters.Select((c, i) =>
            new SourceTrack(c.Title, c.End - c.Start, i == chapters.Length - 1 ? lastShare : share)).ToList();
    }

    private static SourceTrack Merge(IReadOnlyList<SourceTrack> tracks, string bookTitle) =>
        new(bookTitle,
            tracks.Sum(t => t.DurationSeconds),
            tracks.Sum(t => t.EstimatedBytes))
        {
            UnscannedBytes = tracks.Sum(t => t.UnscannedBytes)
        };

    private bool FitsOneTrack(IReadOnlyList<SourceTrack> tracks) =>
        tracks.Sum(t => t.DurationSeconds) <= limits.MaxTrackDurationSeconds &&
        tracks.Sum(t => t.EstimatedBytes) <= limits.MaxTrackBytes;
}
