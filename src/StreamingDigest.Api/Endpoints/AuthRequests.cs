namespace StreamingDigest.Api.Endpoints;

internal sealed record LoginRequest(string Username, string Password);

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);