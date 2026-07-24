### Task 14.1: Select Matrix SDK/implementation

Choose a mature OSS Matrix SDK/service approach. Prefer .NET until feature or stability issues justify Node/Rust/Python or another mature SDK. MVP sends normal unencrypted Matrix messages. E2EE support, Android/device verification, and durable Matrix crypto state are MVP+.

Requirements:

- Dedicated bot account.
- Manual login/token/configuration flow appropriate for the selected SDK.
- Configurable room ID.
- Unencrypted test send for MVP readiness.
- E2EE/encrypted room support, Android/device verification, and E2EE crypto-store backup/restore readiness are MVP+.

Verification:

- Bot can send an unencrypted test message for MVP. Encrypted test message applies only when E2EE is later enabled.

