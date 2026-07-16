using AiAgent.Backend.Entities.Auth;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;

namespace AiAgent.Backend.Services.Auth;

public sealed record AuthenticatedUser(string Id, string Username);

public interface IAuthService
{
    Task<(bool Succeeded, string? Error)> RegisterAsync(string username, string password, CancellationToken cancellationToken);
    Task<(AuthenticatedUser? User, string? Token)> LoginAsync(string username, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> TryGetCurrentUserAsync(HttpContext context, CancellationToken cancellationToken);
    Task LogoutAsync(HttpContext context, CancellationToken cancellationToken);
}

public sealed class AuthService : IAuthService
{
    public const string CookieName = "aiagent_auth";
    private const int Iterations = 210_000;
    private readonly ISqlSugarClient _db;

    public AuthService(ISqlSugarClient db) => _db = db;

    public Task<(bool Succeeded, string? Error)> RegisterAsync(string username, string password, CancellationToken cancellationToken)
    {
        username = username.Trim();
        if (username.Length < 3 || username.Length > 64) return Task.FromResult((false, "账号长度须为 3-64 个字符。"));
        if (password.Length < 6) return Task.FromResult((false, "密码至少需要 6 个字符。"));
        if (_db.Queryable<AiUser>().Any(x => x.Username == username)) return Task.FromResult((false, "该账号已存在。"));

        var salt = RandomNumberGenerator.GetBytes(16);
        var user = new AiUser
        {
            Username = username,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = HashPassword(password, salt)
        };
        _db.Insertable(user).ExecuteCommand();
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
        return Task.FromResult<(AuthenticatedUser?, string?)>((new AuthenticatedUser(user.Id, user.Username), token));
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
        return Task.FromResult(user == null ? null : new AuthenticatedUser(user.Id, user.Username));
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

    private static string HashPassword(string password, byte[] salt) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32));
    private static bool VerifyPassword(string password, string salt, string expected) => CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(HashPassword(password, Convert.FromBase64String(salt))), Convert.FromBase64String(expected));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
