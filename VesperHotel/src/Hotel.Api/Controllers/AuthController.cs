using System.Security.Claims;
using Hotel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hotel.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookie = "vesper.refresh";
    private readonly AuthService auth;
    public AuthController(AuthService auth) { this.auth = auth; }
    [HttpPost("register")] public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    { var session = await auth.RegisterAsync(request); SetCookie(session.RefreshToken); return StatusCode(201, session.Response); }
    [HttpPost("login")] public async Task<AuthResponse> Login(LoginRequest request)
    { var session = await auth.LoginAsync(request); SetCookie(session.RefreshToken); return session.Response; }
    [HttpPost("refresh")] public async Task<AuthResponse> Refresh()
    { var session = await auth.RefreshAsync(Request.Cookies[RefreshCookie]); SetCookie(session.RefreshToken); return session.Response; }
    [HttpPost("logout")] public async Task<IActionResult> Logout()
    { await auth.LogoutAsync(Request.Cookies[RefreshCookie]); Response.Cookies.Delete(RefreshCookie, CookieOptions()); return NoContent(); }
    [Authorize, HttpGet("me")] public Task<UserResponse> Me() => auth.MeAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [Authorize, HttpPut("profile")] public Task<UserResponse> Profile(ProfileRequest request) => auth.ProfileAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, request);
    [HttpPost("forgot-password")] public async Task<object> Forgot(ForgotPasswordRequest request)
    { await auth.ForgotAsync(request.Email); return new { message = "If this email belongs to an account, a reset link will be delivered." }; }
    [HttpPost("reset-password")] public async Task<object> Reset(ResetPasswordRequest request)
    { await auth.ResetAsync(request); return new { message = "Password updated. Please sign in." }; }
    private CookieOptions CookieOptions() => new() { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/api/auth", MaxAge = TimeSpan.FromDays(7), IsEssential = true };
    private void SetCookie(string value) => Response.Cookies.Append(RefreshCookie, value, CookieOptions());
}
