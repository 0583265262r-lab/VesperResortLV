using System.Security.Claims;
using Hotel.Api.Models;
using Hotel.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hotel.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "vesper.refresh";
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request)
    {
        var session = await _authService.RegisterAsync(request);
        SaveRefreshCookie(session.RefreshToken);

        return StatusCode(201, session.Response);
    }

    [HttpPost("login")]
    public async Task<AuthResponse> Login(LoginRequest request)
    {
        var session = await _authService.LoginAsync(request);
        SaveRefreshCookie(session.RefreshToken);

        return session.Response;
    }

    [HttpPost("refresh")]
    public async Task<AuthResponse> Refresh()
    {
        var token = Request.Cookies[RefreshCookieName];
        var session = await _authService.RefreshAsync(token);
        SaveRefreshCookie(session.RefreshToken);

        return session.Response;
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var token = Request.Cookies[RefreshCookieName];
        await _authService.LogoutAsync(token);

        Response.Cookies.Delete(
            RefreshCookieName,
            CreateCookieOptions()
        );

        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<UserResponse> GetCurrentUser()
    {
        return await _authService.MeAsync(GetUserId());
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<UserResponse> UpdateProfile(ProfileRequest request)
    {
        return await _authService.ProfileAsync(GetUserId(), request);
    }

    [HttpPost("forgot-password")]
    public async Task<object> ForgotPassword(ForgotPasswordRequest request)
    {
        await _authService.ForgotAsync(request.Email);

        return new
        {
            message = "If this email belongs to an account, a reset link will be delivered."
        };
    }

    [HttpPost("reset-password")]
    public async Task<object> ResetPassword(ResetPasswordRequest request)
    {
        await _authService.ResetAsync(request);

        return new
        {
            message = "Password updated. Please sign in."
        };
    }

    private string GetUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    }

    private void SaveRefreshCookie(string token)
    {
        Response.Cookies.Append(
            RefreshCookieName,
            token,
            CreateCookieOptions()
        );
    }

    private CookieOptions CreateCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromDays(7),
            IsEssential = true
        };
    }
}
