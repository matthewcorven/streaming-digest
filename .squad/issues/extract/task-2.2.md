### Task 2.2: Implement login/logout/change password

Requirements:

- Secure HTTP-only cookies.
- CSRF protection for mutations.
- Login rate limiting.
- Forced password change when seeded from env.

Verification:

- Auth integration tests pass.
- Mutating endpoint rejects unauthenticated request.

