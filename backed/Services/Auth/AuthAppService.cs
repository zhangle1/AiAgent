using AiAgent.Backend.Dtos.Auth;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Auth;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/auth")]
public sealed class AuthAppService : IDynamicApiController
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;

    public AuthAppService(IHttpContextAccessor httpContextAccessor, IAuthService authService)
    {
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
    }

    [HttpGet("status")]
    public async Task<AuthStatusResponse> Status(CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext!;
        var user = await _authService.TryGetCurrentUserAsync(context, cancellationToken);
        return new AuthStatusResponse { Authenticated = user != null, UserId = user?.Id, Username = user?.Username };
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request.Username, request.Password, cancellationToken);
        return result.Succeeded ? new OkObjectResult(new { ok = true }) : new BadRequestObjectResult(new { message = result.Error });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var (user, token) = await _authService.LoginAsync(request.Username, request.Password, cancellationToken);
        if (user == null || token == null) return new UnauthorizedObjectResult(new { message = "账号或密码错误。" });
        _httpContextAccessor.HttpContext!.Response.Cookies.Append(AuthService.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = _httpContextAccessor.HttpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(14),
            Path = "/"
        });
        return new OkObjectResult(new { ok = true, user_id = user.Id, username = user.Username });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext!;
        await _authService.LogoutAsync(context, cancellationToken);
        context.Response.Cookies.Delete(AuthService.CookieName, new CookieOptions { Path = "/" });
        return new OkObjectResult(new { ok = true });
    }
}
