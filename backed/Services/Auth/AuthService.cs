using AiAgent.Backend.Entities.Auth;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;

namespace AiAgent.Backend.Services.Auth;

public sealed record AuthenticatedUser(string Id, string Username, string Role = "user")
{
    public bool IsAdministrator => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);
}

public interface IAuthService
{
    Task<(bool Succeeded, string? Error)> RegisterAsync(string username, string password, CancellationToken cancellationToken);
    Task<(AiUser? User, string? Error)> CreateUserAsync(string username, string password, string? alias, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> ResetPasswordAsync(string userId, string password, CancellationToken cancellationToken);
    Task<(AuthenticatedUser? User, string? Token)> LoginAsync(string username, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> TryGetCurrentUserAsync(HttpContext context, CancellationToken cancellationToken);
    Task LogoutAsync(HttpContext context, CancellationToken cancellationToken);
    Task EnsureDefaultAdministratorAsync(CancellationToken cancellationToken);
}

public sealed class AuthService : IAuthService
{
    public const string CookieName = "aiagent_auth";
    private const int Iterations = 210_000;
    private readonly ISqlSugarClient _db;

    public AuthService(ISqlSugarClient db) => _db = db;

    public Task<(bool Succeeded, string? Error)> RegisterAsync(string username, string password, CancellationToken cancellationToken)
        => Task.FromResult((false, (string?)"Public registration is disabled. Please ask an administrator to create an account."));

    public Task<(AiUser? User, string? Error)> CreateUserAsync(string username, string password, string? alias, CancellationToken cancellationToken)
    {
        username = username.Trim();
        alias = NormalizeAlias(alias);
        if (username.Length < 3 || username.Length > 64) return Task.FromResult<(AiUser?, string?)>((null, "Username must contain 3-64 characters."));
        if (alias?.Length > 64) return Task.FromResult<(AiUser?, string?)>((null, "Alias must not exceed 64 characters."));
        if (password.Length < 6) return Task.FromResult<(AiUser?, string?)>((null, "Password must contain at least 6 characters."));
        if (_db.Queryable<AiUser>().Any(x => x.Username == username)) return Task.FromResult<(AiUser?, string?)>((null, "The username already exists."));

        var salt = RandomNumberGenerator.GetBytes(16);
        var user = new AiUser
        {
            Username = username,
            Alias = alias,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = HashPassword(password, salt)
        };
        _db.Insertable(user).ExecuteCommand();
        return Task.FromResult<(AiUser?, string?)>((user, null));
    }

    public Task<(bool Succeeded, string? Error)> ResetPasswordAsync(string userId, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult((false, (string?)"The user does not exist."));
        if (password.Length < 6) return Task.FromResult((false, (string?)"Password must contain at least 6 characters."));
        var user = _db.Queryable<AiUser>().First(item => item.Id == userId);
        if (user == null) return Task.FromResult((false, (string?)"The user does not exist."));

        var salt = RandomNumberGenerator.GetBytes(16);
        var passwordSalt = Convert.ToBase64String(salt);
        var passwordHash = HashPassword(password, salt);
        var now = DateTime.UtcNow;
        _db.Updateable<AiUser>()
            .SetColumns(item => item.PasswordSalt == passwordSalt)
            .SetColumns(item => item.PasswordHash == passwordHash)
            .SetColumns(item => item.UpdatedAt == now)
            .Where(item => item.Id == user.Id)
            .ExecuteCommand();
        _db.Updateable<AiUserSession>().SetColumns(item => item.RevokedAt == now)
            .Where(item => item.UserId == user.Id && item.RevokedAt == null).ExecuteCommand();
        return Task.FromResult((true, (string?)null));
    }

    public Task<(AuthenticatedUser? User, string? Token)> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = _db.Queryable<AiUser>().First(x => x.Username == username.Trim());
        if (user == null || user.IsDisabled || !VerifyPassword(password, user.PasswordSalt, user.PasswordHash))
            return Task.FromResult<(AuthenticatedUser?, string?)>((null, null));

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        _db.Insertable(new AiUserSession
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddDays(14)
        }).ExecuteCommand();
        return Task.FromResult<(AuthenticatedUser?, string?)>((new AuthenticatedUser(user.Id, user.Username, user.Role), token));
    }

    public Task<AuthenticatedUser?> TryGetCurrentUserAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrWhiteSpace(token))
            return Task.FromResult<AuthenticatedUser?>(null);
        var tokenHash = HashToken(token);
        var now = DateTime.UtcNow;
        var session = _db.Queryable<AiUserSession>().First(x => x.TokenHash == tokenHash && x.RevokedAt == null && x.ExpiresAt > now);
        if (session == null) return Task.FromResult<AuthenticatedUser?>(null);
        var user = _db.Queryable<AiUser>().First(x => x.Id == session.UserId && !x.IsDisabled);
        return Task.FromResult(user == null ? null : new AuthenticatedUser(user.Id, user.Username, user.Role));
    }

    public Task LogoutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var token) && !string.IsNullOrWhiteSpace(token))
        {
            _db.Updateable<AiUserSession>().SetColumns(x => x.RevokedAt == DateTime.UtcNow)
                .Where(x => x.TokenHash == HashToken(token) && x.RevokedAt == null).ExecuteCommand();
        }
        return Task.CompletedTask;
    }

    public async Task EnsureDefaultAdministratorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string username = "superadmin";
        var user = _db.Queryable<AiUser>().First(item => item.Username == username);
        if (user == null)
        {
            var (created, error) = await CreateUserAsync(username, "123456", null, cancellationToken);
            if (created == null) throw new InvalidOperationException(error ?? "Failed to create the default administrator.");
            created.Role = "admin";
            _db.Updateable(created).UpdateColumns(item => item.Role).ExecuteCommand();
            return;
        }

        if (!string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            _db.Updateable<AiUser>().SetColumns(item => item.Role == "admin").Where(item => item.Id == user.Id).ExecuteCommand();
    }

    private static string HashPassword(string password, byte[] salt) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32));
    private static bool VerifyPassword(string password, string salt, string expected) => CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(HashPassword(password, Convert.FromBase64String(salt))), Convert.FromBase64String(expected));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string? NormalizeAlias(string? alias) => string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
}
