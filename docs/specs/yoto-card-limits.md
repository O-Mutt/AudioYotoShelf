# Spec: Yoto MYO card limits

**Status:** Implemented ([#113](https://github.com/O-Mutt/AudioYotoShelf/issues/113),
[#117](https://github.com/O-Mutt/AudioYotoShelf/issues/117))
**Scope:** the limits `CardCapacityCalculator` checks before building a Make Your Own (MYO) card,
and why they differ from Yoto's published numbers.

## 1. Published limits

Yoto's support article ([How much audio fits on a Make Your Own card?](https://support.yotoplay.com/en_gb/how-much-audio-fits-on-a-make-your-own-card-r1ssdFimfl),
updated 2026-07-20) lists, per card:

- 100 tracks
- 100 MB / 60 minutes for any single track
- 500 MB / 5 hours total

Yoto's [Make Your Own page](https://yotoplay.com/myo) lists the track, per-track and 500 MB limits
but no 5-hour figure.

## 2. Observed behaviour

Measured on 2026-11-24 from cards this app built. Durations and sizes are the values Yoto
returned after transcoding (`TrackMappings.TranscodedDuration` / `TranscodedFileSize`).

| Book | Duration | Tracks | Uploaded (source) | Stored by Yoto (opus) | Bitrate |
|---|---|---|---|---|---|
| Prisoner of Azkaban | 11.54 h | 24 | 629.5 MB | 476.4 MB | ~96 kbps |
| Chamber of Secrets | 9.62 h | 20 | 524.2 MB | 342.8 MB | ~83 kbps |
| Chamber of Secrets (another edition) | 6.53 h | 12 | — | 204.8 MB | ~73 kbps |
| Goblet of Fire (5.1 AAC, 729 kbps source) | 20.23 h | 39 | 6,343 MB (tracks up to 265 MB) | **566.1 MB** | ~65 kbps |

All four cards were accepted. The first three play end to end; Goblet of Fire playback is still to
be checked. So:

- **The 5-hour total is not enforced.**
- **The 500 MB total is not enforced when the card is created.** Goblet of Fire is 566.1 MB as
  stored (593.6 MB in decimal megabytes) and Yoto created the card. The upper limit, if any, is
  unknown.
- **The per-track 100 MB limit is not checked against uploads.** Tracks of up to 265 MB were
  uploaded and stored as at most 23.2 MB.
- **The per-track 60-minute limit is untested.** The longest track was 50.5 minutes.
- **Yoto's bitrate does not follow the source.** A 729 kbps surround source came out at 65 kbps,
  lower than the 127 kbps stereo sources (73–96 kbps).

A [community report](https://yoto.space/questions/post/confused-about-max-limit-for-myo-cards-RbnN8Dq6WZQNJl2)
of an 11 h 34 m / 485 MB card playing agrees.

## 3. What the app enforces

`YotoCardLimits` (config section `Yoto:CardLimits`):

| Key | Default | Checked against |
|---|---|---|
| `MaxTracks` | 100 | projected track count after per-track splitting |
| `MaxTrackBytes` | 100 MB | source bytes of each track. Stricter than Yoto, which accepted 265 MB uploads; kept because the transfer splits tracks by the same rule ([#121](https://github.com/O-Mutt/AudioYotoShelf/issues/121)) |
| `MaxTrackDurationSeconds` | 3600 | each track's duration |
| `MaxTotalBytes` | 500 MB | **estimated transcoded size**: total duration × `TranscodedBitrateKbps` |
| `MaxTotalDurationSeconds` | unset (not enforced) | total duration, only when set |
| `TranscodedBitrateKbps` | 100 | used for the size estimate; a little above the measured 65–96 kbps. A best guess, not a guarantee: Yoto's bitrate does not follow the source |

At 100 kbps, 500 MB holds about 11.6 hours.

To restore the documented 5-hour cap, set `Yoto__CardLimits__MaxTotalDurationSeconds=18000`.

A track with no duration (Audiobookshelf reports 0 for files it has not scanned) counts at its
source size, so an unscanned book cannot slip past the size check. Startup fails if any limit is
zero or negative, or if `TranscodedBitrateKbps` is not greater than 0.

Sizes in the app's messages are MiB (1,048,576 bytes), shown as "MB".

Because the card total is estimated from duration, source size no longer limits a transfer: a
book Yoto stores as 566 MB was 6.3 GB of source. A playlist transfer deletes each book's temp files
once that book's tracks are uploaded, so peak disk use is set by the largest single book, not the
whole playlist ([#128](https://github.com/O-Mutt/AudioYotoShelf/issues/128)).

Within a book, every copy stays on disk until the book is done: the downloaded files, the merged
file or extracted chapters, and the split pieces of any track over the per-track limit. A book
that is merged (or cut into chapters from one file) and then split peaks at about **3× its source
size**; a multi-file book uploaded file by file peaks at 1×, or 2× when files need splitting.

Two folders take this: downloads go to `Transfer:TempDirectory`, and ffmpeg's merged, chapter and
split files go to the system temp folder (`TMPDIR`). The Docker image sets `TMPDIR=/app/temp`, so
both land on the same volume there; outside the image, size both. Partial files left by a failed
download or ffmpeg run are not cleaned up yet
([#131](https://github.com/O-Mutt/AudioYotoShelf/issues/131),
[#132](https://github.com/O-Mutt/AudioYotoShelf/issues/132)).

### Single-book transfers

A book sent on its own (the book page, "One card per book" on a series page, a batch, or a retry)
gets the same check as a playlist, built from the tracks that transfer uploads: one per audio file,
one per chapter of a single-file book, or the whole file. It does not split tracks to the
per-track limits, so the check counts each of those tracks once (`splitTracks: false`), where a
playlist counts the pieces it will split into. The card total is the same estimate as for a
playlist.

Unless the person chooses to send it anyway (below), a book over the limits is never downloaded:

- **Book page:** `POST /api/transfers/book` answers 409 with `message` and `capacity` (the same
  summary a playlist's 409 carries), and queues nothing. The message names the limit the book is
  over, says that a book in a series can be split across cards by the card plan on its series
  page, and points at "Send anyway". The check runs before the duplicate-transfer check and waits
  at most 15 seconds for Audiobookshelf. If Audiobookshelf is slow or unreachable, the transfer is
  queued and the job checks instead.
- **In the job:** `TransferOrchestrator.TransferBookAsync` checks again after reading the book from
  Audiobookshelf, because the book may have changed since it was queued and series, batch and
  retry transfers are not checked before queueing. An oversized book's transfer is marked Failed
  with the same message. Hangfire does not retry it, and it is not counted in the
  `ays.transfers.failed` metric, because the check worked as intended.
- **"One card per book":** the books that fit are sent. A book that does not is reported among
  the series results as a Failed transfer with the message, shown on the Transfers page and as a
  live "Not sent" update. The request itself only says that such books are reported there:
  checking each book before queueing would mean reading every book in the series from
  Audiobookshelf first.

**Send anyway.** The limits are conservative (Yoto created a 566 MB card), and a book outside a
series has no other route onto Yoto. When the book page shows a capacity refusal, it offers "Send
anyway", which posts the book again with `allowOversized: true`. The endpoint then skips its check,
and the job skips its guard. The choice is stored on the transfer (`CardTransfers.AllowOversized`,
migration `AddCardTransferAllowOversized`), so a retry keeps it. A refused transfer is refused
again on retry. Yoto may refuse a card this large, or it may not play to the end. "One card per
book" never sends an oversized book; send it from its book page instead.

## 4. Open questions

- Whether a card over 500 MB plays end to end (Goblet of Fire, 566 MB), and whether any larger
  total is refused. Until both are known the app keeps 500 MB.
- Whether Yoto enforces the 60-minute per-track limit. A single-book transfer uploads a chapter
  longer than that as one track.
