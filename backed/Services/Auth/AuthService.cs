using AiAgent.Backend.Entities.Auth;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;

namespace AiAgent.Backend.Services.Auth;

public sealed record AuthenticatedUser(string Id, string Username, string Role = "user", bool CodeCommitGranted = false)
{
    public bool IsAdministrator => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);
    public bool CanCommitCode => IsAdministrator || CodeCommitGranted;
}

public interface IAuthService
{
    Task<(bool Succeeded, string? Error)> RegisterAsync(string username, string password, CancellationToken cancellationToken);
    Task<(AiUser? User, string? Error)> CreateUserAsync(string username, string password, string? alias, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> ResetPasswordAsync(string userId, string password, CancellationToken cancellationToken);
    Task<(bool Succeeded, string? Error)> ChangePasswordAsync(AuthenticatedUser user, string currentPassword, string newPassword, CancellationToken cancellationToken);
    Task<(AuthenticatedUser? User, string? Token)> LoginAsync(string username, string password, CancellationToken cancellationToken);
    Task<(AuthenticatedUser? User, string? Token)> LoginPluginAsync(string username, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> TryGetCurrentUserAsync(HttpContext context, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> TryGetPluginUserAsync(HttpContext context, CancellationToken cancellationToken);
    Task LogoutAsync(HttpContext context, CancellationToken cancellationToken);
    Task EnsureInitialAdministratorAsync(CancellationToken cancellationToken);
}

public sealed class AuthService : IAuthService
{
    public const string CookieName = "aiagent_auth";
    private const int Iterations = 210_000;
    private readonly ISqlSugarClient _db;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public AuthService(ISqlSugarClient db, IConfiguration configuration, IHostEnvironment environment) =>
        (_db, _configuration, _environment) = (db, configuration, environment);

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

    public Task<(bool Succeeded, string? Error)> ChangePasswordAsync(AuthenticatedUser user, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var account = _db.Queryable<AiUser>().First(item => item.Id == user.Id && !item.IsDisabled);
        if (account == null) return Task.FromResult((false, (string?)"The account is unavailable."));
        if (!VerifyPassword(currentPassword, account.PasswordSalt, account.PasswordHash)) return Task.FromResult((false, (string?)"Current password is incorrect."));
        return ResetPasswordAsync(account.Id, newPassword, cancellationToken);
    }

    public Task<(AuthenticatedUser? User, string? Token)> LoginAsync(string username, string password, CancellationToken cancellationToken)
        => LoginCoreAsync(username, password, TimeSpan.FromDays(14), null, cancellationToken);

    public Task<(AuthenticatedUser? User, string? Token)> LoginPluginAsync(string username, string password, CancellationToken cancellationToken)
        => LoginCoreAsync(username, password, TimeSpan.FromHours(8), "deepseek-plugin", cancellationToken);

    private Task<(AuthenticatedUser? User, string? Token)> LoginCoreAsync(string username, string password, TimeSpan lifetime, string? purpose, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _db.Queryable<AiUser>().First(x => x.Username == username.Trim());
        if (user == null || user.IsDisabled || !VerifyPassword(password, user.PasswordSalt, user.PasswordHash))
            return Task.FromResult<(AuthenticatedUser?, string?)>((null, null));

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        _db.Insertable(new AiUserSession
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.Add(lifetime)
        }).ExecuteCommand();
        return Task.FromResult<(AuthenticatedUser?, string?)>((new AuthenticatedUser(user.Id, user.Username, user.Role, user.CanCommitCode), token));
    }

    public Task<AuthenticatedUser?> TryGetCurrentUserAsync(HttpContext context, CancellationToken cancellationToken)
        => TryGetUserAsync(context, pluginSession: false, cancellationToken);

    public Task<AuthenticatedUser?> TryGetPluginUserAsync(HttpContext context, CancellationToken cancellationToken)
        => TryGetUserAsync(context, pluginSession: true, cancellationToken);

    private Task<AuthenticatedUser?> TryGetUserAsync(HttpContext context, bool pluginSession, CancellationToken cancellationToken)
    {
        var token = ResolveToken(context);
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult<AuthenticatedUser?>(null);
        var tokenHash = HashToken(token);
        var now = DateTime.UtcNow;
        var session = pluginSession
            ? _db.Queryable<AiUserSession>().First(x => x.TokenHash == tokenHash && x.Purpose == "deepseek-plugin" && x.RevokedAt == null && x.ExpiresAt > now)
            : _db.Queryable<AiUserSession>().First(x => x.TokenHash == tokenHash && x.Purpose == null && x.RevokedAt == null && x.ExpiresAt > now);
        if (session == null) return Task.FromResult<AuthenticatedUser?>(null);
        var user = _db.Queryable<AiUser>().First(x => x.Id == session.UserId && !x.IsDisabled);
        return Task.FromResult(user == null ? null : new AuthenticatedUser(user.Id, user.Username, user.Role, user.CanCommitCode));
    }

    public Task LogoutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var token = ResolveToken(context);
        if (!string.IsNullOrWhiteSpace(token))
        {
            _db.Updateable<AiUserSession>().SetColumns(x => x.RevokedAt == DateTime.UtcNow)
                .Where(x => x.TokenHash == HashToken(token) && x.RevokedAt == null).ExecuteCommand();
        }
        return Task.CompletedTask;
    }

    public async Task EnsureInitialAdministratorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_db.Queryable<AiUser>().Any(item => item.Role == "admin" && !item.IsDisabled)) return;

        var username = _configuration["Authentication:InitialAdministratorUsername"]?.Trim();
        var password = _configuration["Authentication:InitialAdministratorPassword"];
        var generatedLocalDevelopmentPassword = false;

        // Local development may start with an empty database. Never use a fixed password,
        // and only disclose the generated one to an interactive, non-redirected terminal.
        if (string.IsNullOrWhiteSpace(username)
            && string.IsNullOrEmpty(password)
            && _environment.IsDevelopment()
            && !Console.IsErrorRedirected)
        {
            username = "superadmin";
            password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            generatedLocalDevelopmentPassword = true;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("No usable administrator exists. Set Authentication:InitialAdministratorUsername and Authentication:InitialAdministratorPassword before first startup.");
        var isLocalDevelopmentSixDigitPassword = _environment.IsDevelopment()
            && password.Length == 6
            && password.All(char.IsAsciiDigit);
        if (password.Length < 16 && !isLocalDevelopmentSixDigitPassword)
            throw new InvalidOperationException("Authentication:InitialAdministratorPassword must contain at least 16 characters, except for exactly six numeric digits in local Development.");
        if (_db.Queryable<AiUser>().Any(item => item.Username == username))
            throw new InvalidOperationException("Authentication:InitialAdministratorUsername is already in use. Choose an unused username or recover an existing administrator.");

        var (created, error) = await CreateUserAsync(username, password, null, cancellationToken);
        if (created == null) throw new InvalidOperationException(error ?? "Failed to create the initial administrator.");
        _db.Updateable<AiUser>().SetColumns(item => item.Role == "admin").Where(item => item.Id == created.Id).ExecuteCommand();

        if (generatedLocalDevelopmentPassword)
        {
            Console.Error.WriteLine($"[AiAgent] Local development administrator created. Username: {username}; one-time password: {password}");
        }
    }

    private static string HashPassword(string password, byte[] salt) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32));
    private static bool VerifyPassword(string password, string salt, string expected) => CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(HashPassword(password, Convert.FromBase64String(salt))), Convert.FromBase64String(expected));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string? ResolveToken(HttpContext context)
    {
        if (context.Request.Headers.Authorization.Count == 1)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var bearer = authorization["Bearer ".Length..].Trim();
                if (bearer.Length > 0) return bearer;
            }
        }
        return context.Request.Cookies.TryGetValue(CookieName, out var cookie) ? cookie : null;
    }
    private static string? NormalizeAlias(string? alias) => string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
}
