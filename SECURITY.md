# Security Policy

## Reporting a vulnerability

Please open a GitHub issue marked as a security question, or contact the maintainer directly
(see the GitHub profile of `chenran0408`). Do not disclose details publicly before a fix ships.

## Scope notes

- Lertaro runs three processes on a machine: an indexer **Windows service (SYSTEM)**, the user
  UI, and an elevated keyboard-hook process. The service/hook boundary is IPC over named pipes;
  any report about privilege boundaries or the pipe ACLs is especially welcome.
- Clipboard history is stored **locally and unencrypted** (SQLite + PNG files) in the user data
  directory; a password-manager process blacklist is available in the plugin settings. If you
  believe a stronger default is required, please open an issue first.
- Release assets are signed with a key whose public half ships as
  `release-signing-public-key.pem`; report any asset that fails that verification.
