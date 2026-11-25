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
        // A malformed chapter (End < Start) must not shrink the divisor or carry negative bytes.
        var spans = chapters.Select(c => Math.Max(0, c.End - c.Start)).ToArray();

        // Chapter spans stand in for an unscanned file's length only when every chapter is timed:
        // one untimed chapter means the spans may miss audio, and the bytes it would leave out
        // could let a large book slip past the size check.
        var totalDuration = file.Duration > 0 ? file.Duration
            : bookDuration > 0 ? bookDuration
            : spans.All(s => s > 0) ? spans.Sum() : 0;

        if (totalDuration > 0)
        {
            var tracks = new List<SourceTrack>(chapters.Length);
            var elapsed = 0.0;
            var bytesBefore = 0L;
            for (var i = 0; i < chapters.Length; i++)
            {
                elapsed += spans[i];
                // Rounding the running boundary rather than each share keeps the shares summing to
                // the file when the chapters cover it, yet leaves audio they do not cover unclaimed.
                var bytesAfter = Math.Min(file.Size, (long)Math.Round(file.Size * (elapsed / totalDuration)));
                tracks.Add(new SourceTrack(chapters[i].Title, spans[i], bytesAfter - bytesBefore));
                bytesBefore = bytesAfter;
            }
            return tracks;
        }

        // With no duration at all, every share counts in full as unscanned size; the last takes
        // the rounding remainder so the card total holds the file exactly once.
        var share = file.Size / chapters.Length;
        var lastShare = file.Size - share * (chapters.Length - 1);
        return chapters.Select((c, i) =>
            new SourceTrack(c.Title, 0, i == chapters.Length - 1 ? lastShare : share)).ToList();
    }

    private static SourceTrack Merge(IReadOnlyList<SourceTrack> tracks, string bookTitle) =>
        new(bookTitle,
            tracks.Sum(t => t.DurationSeconds),
            tracks.Sum(t => t.EstimatedBytes))
        {
            UnscannedBytes = tracks.Sum(t => t.UnscannedBytes)
        };

    // An unscanned track's length is unknown, so its zero duration cannot prove the book fits one track.
    // The byte test uses the same stored-size estimate as CardCapacityCalculator.ProjectedTrackCount,
    // so a book Auto merges always projects to exactly one track and is never split again.
    private bool FitsOneTrack(IReadOnlyList<SourceTrack> tracks)
    {
        if (tracks.Any(t => t.UnscannedBytes != 0)) return false;
        var duration = tracks.Sum(t => t.DurationSeconds);
        return duration <= limits.MaxTrackDurationSeconds &&
               limits.EstimateTranscodedBytes(duration) <= limits.MaxTrackBytes;
    }
}
