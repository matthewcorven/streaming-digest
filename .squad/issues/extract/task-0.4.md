### Task 0.4: Establish test fixture library

Create a shared fixture library used by unit/integration/acceptance tests:

- Recorded yt-dlp channel/video metadata JSON, including chapters and captions variants.
- Caption/transcript fixtures with timestamps.
- Bundled tiny audio clip for audio-to-text tests.
- Short test video fixture for screenshot generation.
- Local test HTML page and controlled `robots.txt` fixtures.
- GitHub repository metadata/README/LICENSE fixtures, including missing-document variants.
- DeepWiki reachable-page and `Index your code` placeholder fixtures.
- Rate-limit (429 + `Retry-After`) fixtures for YouTube, repository hosts, DeepWiki, and website hosts.
- URL normalization/classification corpus with tracking parameters and redirect chains.
- Representative vague-query corpus with expected video-cluster mappings for the recall harness (Task 12.7).

Verification:

- Every fixture above loads in at least one test.
- Fixture provenance/licensing notes are recorded in the fixture README.

