using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Domain.Errors;

public static class AuthErrors
{
    public static readonly Error IdentityConflict = new(
        "auth.identity_conflict", "The user identity changed concurrently. Reload it before retrying.", ErrorKind.Conflict);

    public static readonly Error InvalidIdentityVersion = new(
        "auth.invalid_identity_version", "The identity version cannot be incremented.", ErrorKind.Failure);

    public static readonly Error AccountExists = new(
        "auth.account_exists", "User name or email already exists.", ErrorKind.Conflict);

    public static readonly Error UserNameAlreadyExists = new(
        "auth.account_exists", "User name already exists.", ErrorKind.Conflict, "UserName");

    public static readonly Error EmailAlreadyExists = new(
        "auth.account_exists", "Email already exists.", ErrorKind.Conflict, "Email");

    public static readonly Error InvalidCredentials = new(
        "auth.invalid_credentials", "Invalid credentials.", ErrorKind.Unauthorized);

    public static readonly Error InvalidRefreshToken = new(
        "auth.invalid_refresh_token", "Invalid or expired refresh token.", ErrorKind.Unauthorized);

    public static readonly Error UserNameRequired = new(
        "auth.username_required", "User name is required.", ErrorKind.Validation, "UserName");

    public static readonly Error InvalidUserName = new(
        "auth.invalid_username", "User name must contain between 3 and 100 characters after trimming.", ErrorKind.Validation, "UserName");

    public static readonly Error EmailRequired = new(
        "auth.email_required", "Email is required.", ErrorKind.Validation, "Email");

    public static readonly Error InvalidEmail = new(
        "auth.invalid_email", "Email must contain at most 256 characters after trimming.", ErrorKind.Validation, "Email");

    public static readonly Error UserRequired = new(
        "auth.user_required", "A user account with an assigned ID is required.", ErrorKind.Failure);

    public static readonly Error SessionRequired = new(
        "auth.session_required", "A refresh session with an assigned ID is required.", ErrorKind.Failure);

    public static readonly Error PasswordHashRequired = new(
        "auth.password_hash_required", "A password hash is required.", ErrorKind.Failure);

    public static readonly Error InvalidTokenHash = new(
        "auth.invalid_token_hash", "A nonblank token hash of at most 64 characters is required.", ErrorKind.Failure);

    public static readonly Error TokenHashMismatch = new(
        "auth.token_hash_mismatch", "Token history must match the session's current token hash.", ErrorKind.Failure);

    public static readonly Error TokenHashUnchanged = new(
        "auth.token_hash_unchanged", "Token rotation requires a different hash.", ErrorKind.Failure);

    public static readonly Error InvalidUtcTimestamp = new(
        "auth.invalid_utc_timestamp", "A nondefault UTC timestamp is required.", ErrorKind.Failure);
}
