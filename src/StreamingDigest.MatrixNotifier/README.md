# Streaming Digest Matrix notifier

The MVP Matrix notifier uses the Matrix Client-Server API directly over HTTPS from .NET, which keeps the implementation simple and avoids a separate runtime dependency while still supporting unencrypted room sends.

## Manual setup for a dedicated bot account

1. Create a dedicated Matrix bot account for Streaming Digest.
2. Log in with a Matrix client and copy the access token from the client settings or account manager UI.
3. Create or join the target room and copy the room ID (for example `!abc123:matrix.org`).
4. Configure the following values in app settings:

```json
{
  "notifications": {
    "matrix": {
      "enabled": true,
      "homeserverUrl": "https://matrix-client.matrix.org",
      "botUserId": "@streamingdigest:matrix.org",
      "roomId": "!your-room-id:matrix.org",
      "accessToken": "syt_...",
      "onManualRuns": true,
      "onScheduledRuns": true,
      "onBackfillRuns": false,
      "dashboardBaseUrl": "http://localhost:8080"
    }
  }
}
```

The client sends plain unencrypted messages for MVP. End-to-end encryption remains MVP+ and is not enabled by this implementation.
