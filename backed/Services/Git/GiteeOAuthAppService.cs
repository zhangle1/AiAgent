using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using AiAgent.Backend.Dtos.Git;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace AiAgent.Backend.Services.Git;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/gitee-oauth")]
public sealed class GiteeOAuthAppService : IDynamicApiController
{
    private const string StateCookie = "aiagent_gitee_oauth_state";
    private const string AuthorizeEndpoint = "https://gitee.com/oauth/authorize";
    private const string TokenEndpoint = "https://gitee.com/oauth/token";
    private const string UserEndpoint = "https://gitee.com/api/v5/user";
    private readonly ISqlSugarClient _db;
    private readonly IHttpContextAccessor _context;
    private readonly IAuthService _auth;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;

    public GiteeOAuthAppService(ISqlSugarClient db, IHttpContextAccessor context, IAuthService auth, IDataProtectionProvider protection, IHttpClientFactory http, IConfiguration configuration)
        => (_db, _context, _auth, _protector, _http, _configuration) = (db, context, auth, protection.CreateProtector("AiAgent.GitAccounts.AccessToken.v1"), http, configuration);

    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        await RequireUser(cancellationToken);
        var clientId = Configuration("ClientId");
        var redirectUri = Configuration("RedirectUri");
        if (clientId is null || redirectUri is null) return new BadRequestObjectResult(new { message = "Gitee OAuth 尚未配置 ClientId 和 RedirectUri，请通过部署环境注入 GiteeOAuth 配置。" });
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _context.HttpContext!.Response.Cookies.Append(StateCookie, state, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = _context.HttpContext.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddMinutes(10), Path = "/api/v1/gitee-oauth" });
        var url = $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString(Configuration("Scopes") ?? "user_info projects issues enterprises")}&state={Uri.EscapeDataString(state)}";
        return new OkObjectResult(new GiteeOAuthAuthorizeResponse { AuthorizeUrl = url });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error)) return OAuthPage(false, "Gitee 用户取消了授权或授权失败。");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) || !_context.HttpContext!.Request.Cookies.TryGetValue(StateCookie, out var expected) || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(state), System.Text.Encoding.UTF8.GetBytes(expected ?? string.Empty))) return OAuthPage(false, "OAuth state 校验失败，请重新发起 Gitee 登录。");
        _context.HttpContext.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/api/v1/gitee-oauth" });
        var user = await RequireUser(cancellationToken);
        var clientId = Configuration("ClientId"); var clientSecret = Configuration("ClientSecret"); var redirectUri = Configuration("RedirectUri");
        if (clientId is null || clientSecret is null || redirectUri is null) return OAuthPage(false, "Gitee OAuth 服务端配置不完整。");
        try
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = clientId, ["redirect_uri"] = redirectUri, ["client_secret"] = clientSecret }) };
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var tokenResponse = await _http.CreateClient().SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode) return OAuthPage(false, $"Gitee 换取令牌失败（HTTP {(int)tokenResponse.StatusCode}）。请检查回调地址是否与 Gitee 应用配置完全一致。");
            var tokenPayload = JsonSerializer.Deserialize<GiteeOAuthTokenResponse>(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(tokenPayload?.AccessToken)) return OAuthPage(false, "Gitee 未返回 access_token，请检查 OAuth 应用权限。");
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("token", tokenPayload.AccessToken);
            userRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgent", "1.0"));
            using var userResponse = await _http.CreateClient().SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode) return OAuthPage(false, $"Gitee 用户信息读取失败（HTTP {(int)userResponse.StatusCode}），请检查 user_info 权限。");
            var giteeUser = JsonSerializer.Deserialize<GiteeOAuthUser>(await userResponse.Content.ReadAsStringAsync(cancellationToken));
            var username = (giteeUser?.Login ?? giteeUser?.Name)?.Trim();
            if (string.IsNullOrWhiteSpace(username)) return OAuthPage(false, "Gitee 用户信息缺少 login。");
            SaveAccount(user, username, giteeUser?.Name, giteeUser?.Email, tokenPayload.AccessToken);
            return OAuthPage(true, "Gitee 登录成功，令牌已安全保存到当前用户的 Gitee 账号。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OAuthPage(false, "连接 Gitee 超时，请检查服务器网络后重试。");
        }
        catch (HttpRequestException)
        {
            return OAuthPage(false, "无法连接 Gitee，请检查服务器网络或代理配置后重试。");
        }
        catch (JsonException)
        {
            return OAuthPage(false, "Gitee 返回了无法识别的数据，请稍后重试。");
        }
    }

    private void SaveAccount(AuthenticatedUser user, string username, string? name, string? email, string token)
    {
        var now = DateTime.UtcNow;
        var account = _db.Queryable<AiGitAccount>().First(x => x.UserId == user.Id && x.Provider == "gitee" && x.Username == username && !x.IsDeleted);
        _db.Updateable<AiGitAccount>().SetColumns(x => x.IsActive == false).SetColumns(x => x.UpdatedAt == now).Where(x => x.UserId == user.Id && x.Provider == "gitee" && !x.IsDeleted).ExecuteCommand();
        if (account is null)
        {
            _db.Insertable(new AiGitAccount { UserId = user.Id, Provider = "gitee", DisplayName = (name ?? username).Trim(), Username = username, Email = NormalizeOptional(email), AccessTokenProtected = _protector.Protect(token), IsActive = true, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
            return;
        }
        account.DisplayName = (name ?? account.DisplayName ?? username).Trim(); account.Email = NormalizeOptional(email) ?? account.Email; account.AccessTokenProtected = _protector.Protect(token); account.IsActive = true; account.UpdatedAt = now;
        _db.Updateable(account).UpdateColumns(x => new { x.DisplayName, x.Email, x.AccessTokenProtected, x.IsActive, x.UpdatedAt }).ExecuteCommand();
    }

    private async Task<AuthenticatedUser> RequireUser(CancellationToken cancellationToken) => await _auth.TryGetCurrentUserAsync(_context.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
    private string? Configuration(string key) => string.IsNullOrWhiteSpace(_configuration[$"GiteeOAuth:{key}"]) ? null : _configuration[$"GiteeOAuth:{key}"]!.Trim();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ContentResult OAuthPage(bool success, string message) => new() { ContentType = "text/html; charset=utf-8", Content = $"<!doctype html><meta charset=\"utf-8\"><title>Gitee OAuth</title><p>{WebUtility.HtmlEncode(message)}</p><script>window.opener?.postMessage({{type:'aiagent:gitee-oauth',ok:{success.ToString().ToLowerInvariant()}}},'*');window.setTimeout(()=>window.close(),300);</script>" };
}
