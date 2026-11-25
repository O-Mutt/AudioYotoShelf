# Spec: transfer retry resilience

**Status:** Implemented (issues #115, #116)
**Scope:** how a book transfer survives a failed transcode status poll, and what a retried transfer
reuses from its earlier attempt.

## 1. Problem

A 20-hour, 39-track transfer failed on track 28 because one transcode status poll got
`Connection reset by peer`. The automatic retry then deleted every track mapping and uploaded all 39
tracks again, which added about 80 minutes.

## 2. Transcode status polls

`YotoService.PollTranscodeStatusAsync` polls `GET /media/upload/{uploadId}/transcoded` every 5 s.
A GET is safe to repeat, so a failed poll is retried with a 2 / 4 / 8 / 16 s backoff when it is
transient:

| Failure | Retried |
| --- | --- |
| No response at all (connection reset, DNS failure, refused connection) | yes |
| Client timeout (`HttpClient.Timeout`) | yes |
| 408, 429, 500, 502, 503, 504 | yes |
| Any other status, such as 401 or 404 | no, fails at once |
| The caller's own cancellation | no |

- Five failures in a row fail the poll; one good poll resets the count.
- When the five failures are all client timeouts, the poll throws `TimeoutException`, not
  `TaskCanceledException`. A cancellation would mark the transfer Cancelled, and a Cancelled
  transfer is never retried.
- `Retry-After` is not read; the fixed backoff applies to 429 and 503 too.

## 3. What a retry reuses

A retry (automatic or `RetryTransferAsync`) keeps the transfer's track mappings. When
`CreateTrackMappingsAsync` builds the track list again:

- A track from the earlier attempt is reused when it has **both** a transcoded SHA and a
  `TranscodedFormat`, and its file inode and start/end times match the new track. It is neither
  extracted nor uploaded again.
- A row with a SHA but no format is treated as unfinished. Rows saved before formats were recorded
  have none, and the card would declare the `aac` fallback for audio Yoto may have stored as opus,
  which breaks playback.
- Rows that are unfinished or no longer match the book are deleted.
- For a single-file book cut into chapters, the source file is downloaded only when a chapter still
  has to be cut. A retry whose chapters are all on Yoto downloads nothing.
- A reused track advances the progress bar the same as an uploaded one.

## 4. Known gaps

- The Yoto access token is fetched once per run. A token that expires during a very long transfer
  fails the next call with 401.
- Two overlapping runs of the same transfer can delete each other's unfinished rows.
