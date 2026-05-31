# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report privately through GitHub's coordinated disclosure:

1. Go to the [Security tab](https://github.com/O-Mutt/AudioYotoShelf/security/advisories/new).
2. Click **Report a vulnerability**.
3. Describe the issue, affected versions, and steps to reproduce.

You can expect an initial response within a few days. Once a fix is available,
we'll coordinate disclosure and credit you if you'd like.

## Supported versions

This project ships from `main`. Security fixes are applied to the latest
released version and `main`; older versions are not maintained.

## Handling secrets

- Never commit `.env` files, API keys, tokens, or credentials. `.env` is
  git-ignored; use [`.env.example`](.env.example) as the template.
- The following are provided via environment variables at runtime, never
  hardcoded: `YOTO_CLIENT_ID`, `YOTO_CLIENT_SECRET`, `GEMINI_API_KEY`,
  `DB_PASSWORD`.
- Audiobookshelf and Yoto access tokens are stored per-user in the database;
  treat database backups as sensitive.
- When attaching logs to an issue, redact tokens and authorization headers.
