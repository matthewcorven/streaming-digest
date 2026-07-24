### Task 2.1: Implement bootstrap admin user

Requirements:

- Read bootstrap username/password from environment variables at first startup.
- Hash with Argon2id.
- Store in `app_users`.
- Set `must_change_password=true`.

Verification:

- Startup creates user once.
- Password is not stored plaintext.

