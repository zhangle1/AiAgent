using AiAgent.Backend.Dtos.Git;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using SqlSugar;

namespace AiAgent.Backend.Services.Git;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/git-accounts")]
public sealed class GitAccountAppService : IDynamicApiController
{
    private readonly ISqlSugarClient _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;

    public GitAccountAppService(
        ISqlSugarClient db,
        IHttpContextAccessor httpContextAccessor,
        IAuthService authService,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
        _protector = dataProtectionProvider.CreateProtector("AiAgent.GitAccounts.AccessToken.v1");
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("list")]
    public async Task<object> List(CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var accounts = _db.Queryable<AiGitAccount>()
            .Where(x => x.UserId == user.Id && !x.IsDeleted)
            .OrderByDescending(x => x.IsActive)
            .OrderBy(x => x.Provider == "gitee" ? 0 : 1)
            .OrderByDescending(x => x.UpdatedAt)
            .ToList()
            .Select(ToDto)
            .ToList();
        return new { accounts };
    }

    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] GitAccountPayload payload, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var validation = Validate(payload);
        if (validation is not null) return new BadRequestObjectResult(new { message = validation });

        var provider = NormalizeProvider(payload.Provider);
        var username = payload.Username.Trim();
        if (_db.Queryable<AiGitAccount>().Any(x => x.UserId == user.Id && x.Provider == provider && x.Username == username && !x.IsDeleted))
            return new ConflictObjectResult(new { message = "This Git account is already configured." });

        var now = DateTime.UtcNow;
        var account = new AiGitAccount
        {
            UserId = user.Id,
            Provider = provider,
            DisplayName = payload.DisplayName.Trim(),
            Username = username,
            Email = NormalizeOptional(payload.Email),
            AccessTokenProtected = string.IsNullOrWhiteSpace(payload.AccessToken) ? null : _protector.Protect(payload.AccessToken.Trim()),
            IsActive = payload.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (account.IsActive) DeactivateOtherAccounts(user.Id);
        _db.Insertable(account).ExecuteCommand();
        return new OkObjectResult(new { account = ToDto(account) });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] GitAccountPayload payload, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var account = _db.Queryable<AiGitAccount>().First(x => x.Id == id && x.UserId == user.Id && !x.IsDeleted);
        if (account is null) return new NotFoundObjectResult(new { message = "Git account was not found." });

        var validation = Validate(payload);
        if (validation is not null) return new BadRequestObjectResult(new { message = validation });

        var provider = NormalizeProvider(payload.Provider);
        var username = payload.Username.Trim();
        if (_db.Queryable<AiGitAccount>().Any(x => x.UserId == user.Id && x.Id != id && x.Provider == provider && x.Username == username && !x.IsDeleted))
            return new ConflictObjectResult(new { message = "This Git account is already configured." });

        account.Provider = provider;
        account.DisplayName = payload.DisplayName.Trim();
        account.Username = username;
        account.Email = NormalizeOptional(payload.Email);
        account.IsActive = payload.IsActive;
        account.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(payload.AccessToken)) account.AccessTokenProtected = _protector.Protect(payload.AccessToken.Trim());
        if (account.IsActive) DeactivateOtherAccounts(user.Id, account.Id);
        _db.Updateable(account)
            .UpdateColumns(x => new { x.Provider, x.DisplayName, x.Username, x.Email, x.AccessTokenProtected, x.IsActive, x.UpdatedAt })
            .ExecuteCommand();
        return new OkObjectResult(new { account = ToDto(account) });
    }

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(long id, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var account = _db.Queryable<AiGitAccount>().First(x => x.Id == id && x.UserId == user.Id && !x.IsDeleted);
        if (account is null) return new NotFoundObjectResult(new { message = "Git account was not found." });

        DeactivateOtherAccounts(user.Id, account.Id);
        var updatedAt = DateTime.UtcNow;
        _db.Updateable<AiGitAccount>()
            .SetColumns(x => x.IsActive == true)
            .SetColumns(x => x.UpdatedAt == updatedAt)
            .Where(x => x.Id == account.Id)
            .ExecuteCommand();
        account.IsActive = true;
        account.UpdatedAt = updatedAt;
        return new OkObjectResult(new { account = ToDto(account) });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var updatedAt = DateTime.UtcNow;
        var count = _db.Updateable<AiGitAccount>()
            .SetColumns(x => x.IsDeleted == true)
            .SetColumns(x => x.UpdatedAt == updatedAt)
            .Where(x => x.Id == id && x.UserId == user.Id && !x.IsDeleted)
            .ExecuteCommand();
        return count > 0
            ? new OkObjectResult(new { deleted = true })
            : new NotFoundObjectResult(new { message = "Git account was not found." });
    }

    [HttpPost("{id}/test")]
    public async Task<IActionResult> Test(long id, CancellationToken cancellationToken)
    {
        var user = await RequireUser(cancellationToken);
        var account = _db.Queryable<AiGitAccount>().First(x => x.Id == id && x.UserId == user.Id && !x.IsDeleted);
        if (account is null) return new NotFoundObjectResult(new { message = "Git account was not found." });
        if (string.IsNullOrWhiteSpace(account.AccessTokenProtected))
            return new BadRequestObjectResult(new { message = "Configure an access token before running a connection test." });

        string accessToken;
        try
        {
            accessToken = _protector.Unprotect(account.AccessTokenProtected);
        }
        catch
        {
            return new BadRequestObjectResult(new { message = "The stored access token cannot be read. Save the account with a new token." });
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, account.Provider == "github" ? "https://api.github.com/user" : "https://gitee.com/api/v5/user");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgent", "1.0"));
        request.Headers.Authorization = account.Provider == "github"
            ? new AuthenticationHeaderValue("Bearer", accessToken)
            : new AuthenticationHeaderValue("token", accessToken);

        try
        {
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            var result = new GitAccountTestResult
            {
                TestedAt = DateTime.UtcNow,
                Status = response.IsSuccessStatusCode ? "success" : "failed",
                Summary = response.IsSuccessStatusCode ? $"{account.Provider} connection test passed." : $"{account.Provider} rejected the connection test.",
                Detail = response.IsSuccessStatusCode
                    ? $"Account @{account.Username} was authenticated successfully."
                    : $"Remote service returned HTTP {(int)response.StatusCode}. Check the access token and its permissions."
            };
            return new OkObjectResult(new { result });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new OkObjectResult(new
            {
                result = new GitAccountTestResult
                {
                    TestedAt = DateTime.UtcNow,
                    Status = "failed",
                    Summary = $"Could not connect to {account.Provider}.",
                    Detail = "Check the server network connection and try again."
                }
            });
        }
    }

    private void DeactivateOtherAccounts(string userId, long exceptId = 0)
    {
        var updatedAt = DateTime.UtcNow;
        _db.Updateable<AiGitAccount>()
            .SetColumns(x => x.IsActive == false)
            .SetColumns(x => x.UpdatedAt == updatedAt)
            .Where(x => x.UserId == userId && x.Id != exceptId && !x.IsDeleted && x.IsActive)
            .ExecuteCommand();
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken)
        => await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken)
            ?? throw new UnauthorizedAccessException();

    private static GitAccountDto ToDto(AiGitAccount account) => new()
    {
        Id = account.Id,
        Provider = account.Provider,
        DisplayName = account.DisplayName,
        Username = account.Username,
        Email = account.Email,
        TokenConfigured = !string.IsNullOrWhiteSpace(account.AccessTokenProtected),
        IsActive = account.IsActive,
        UpdatedAt = account.UpdatedAt
    };

    private static string NormalizeProvider(string? provider)
        => provider?.Trim().ToLowerInvariant() == "github" ? "github" : "gitee";

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Validate(GitAccountPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.DisplayName) || payload.DisplayName.Trim().Length > 128)
            return "Account name is required and must be no longer than 128 characters.";
        if (string.IsNullOrWhiteSpace(payload.Username) || payload.Username.Trim().Length > 128)
            return "Username is required and must be no longer than 128 characters.";
        if (payload.Provider is not null && payload.Provider.Trim().Length > 32)
            return "This Git provider is not supported.";
        return null;
    }
}
