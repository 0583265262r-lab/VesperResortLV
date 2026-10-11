using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Hotel.Api.Data;
using Hotel.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Hotel.Api.Services;

public class AuthService
{
    private readonly HotelDbContext _db;
    private readonly UserManager<HotelUser> _userManager;
    private readonly SignInManager<HotelUser> _signInManager;
    private readonly IConfiguration _configuration;

    public AuthService(
        HotelDbContext db,
        UserManager<HotelUser> userManager,
        SignInManager<HotelUser> signInManager,
        IConfiguration configuration)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
    }

    public async Task<AuthSession> RegisterAsync(RegisterRequest request)
    {
        await using var transaction = await DatabaseWrite.BeginAsync(_db, "system");

        var user = new HotelUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.PhoneNumber.Trim()
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors.Select(error => error.Description));
            throw new HotelException(400, "registration_failed", message);
        }

        await _userManager.AddToRoleAsync(user, "Guest");
        var session = await CreateSessionAsync(user);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return session;
    }

    public async Task<AuthSession> LoginAsync(LoginRequest request)
    {
        await using var transaction = await DatabaseWrite.BeginAsync(_db, "system");

        var user = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (user == null)
        {
            throw new HotelException(401, "invalid_credentials", "Email or password is incorrect.");
        }

        var result = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            await transaction.CommitAsync();

            var message = result.IsLockedOut
                ? "Too many attempts. Try again in 15 minutes."
                : "Email or password is incorrect.";

            throw new HotelException(401, "invalid_credentials", message);
        }

        var session = await CreateSessionAsync(user);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return session;
    }

    public async Task<AuthSession> RefreshAsync(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new HotelException(401, "session_expired", "Please sign in again.");
        }

        await using var transaction = await DatabaseWrite.BeginAsync(_db, "system");

        var tokenHash = BookingService.Hash(token);
        var oldSession = await _db.RefreshSessions
            .SingleOrDefaultAsync(session => session.TokenHash == tokenHash);

        var sessionIsInvalid = oldSession == null
            || oldSession.RevokedAtUtc != null
            || oldSession.ExpiresAtUtc <= DateTime.UtcNow;

        if (sessionIsInvalid)
        {
            throw new HotelException(401, "session_expired", "Please sign in again.");
        }

        var user = await _userManager.FindByIdAsync(oldSession!.UserId);
        if (user == null)
        {
            throw new HotelException(401, "session_expired", "Please sign in again.");
        }

        oldSession.RevokedAtUtc = DateTime.UtcNow;
        var newSession = await CreateSessionAsync(user);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return newSession;
    }

    public async Task LogoutAsync(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        await using var transaction = await DatabaseWrite.BeginAsync(_db, "system");

        var tokenHash = BookingService.Hash(token);
        var session = await _db.RefreshSessions
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash);

        if (session != null)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<UserResponse> MeAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            throw new HotelException(401, "user_not_found", "Please sign in again.");
        }

        return await CreateUserResponseAsync(user);
    }

    public async Task<UserResponse> ProfileAsync(string userId, ProfileRequest request)
    {
        await using var transaction = await DatabaseWrite.BeginAsync(_db, "system");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            throw new HotelException(401, "user_not_found", "Please sign in again.");
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = request.PhoneNumber.Trim();

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new HotelException(400, "profile_failed", "The profile could not be saved.");
        }

        await transaction.CommitAsync();
        return await CreateUserResponseAsync(user);
    }

    public async Task ForgotAsync(string email)
    {
        await using var transaction = await DatabaseWrite.BeginAsync(_db, "system");

        var cleanEmail = email.Trim();
        var user = await _userManager.FindByEmailAsync(cleanEmail);

        if (user != null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var frontendUrl = (_configuration["Frontend:Url"] ?? "http://localhost:5173").TrimEnd('/');
            var resetUrl = frontendUrl
                + "/reset-password?email=" + Uri.EscapeDataString(cleanEmail)
                + "&token=" + Uri.EscapeDataString(token);

            var message = new NotificationMessage
            {
                Subject = "Reset your VESPER password",
                Message = "Use this private link within one hour: " + resetUrl
            };

            _db.Outbox.Add(new OutboxEvent
            {
                Type = "PasswordReset",
                UserId = user.Id,
                PayloadJson = JsonDefaults.Serialize(message)
            });

            await _db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task ResetAsync(ResetPasswordRequest request)
    {
        await using var transaction = await DatabaseWrite.BeginAsync(_db, "system");

        var user = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (user == null)
        {
            throw new HotelException(400, "invalid_reset", "The reset link is invalid or expired.");
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new HotelException(
                400,
                "invalid_reset",
                "The reset link is invalid, expired, or the password does not meet requirements.");
        }

        var activeSessions = await _db.RefreshSessions
            .Where(session => session.UserId == user.Id && session.RevokedAtUtc == null)
            .ToListAsync();

        foreach (var session in activeSessions)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<object> ListUsersAsync(int page, int pageSize, string? search)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(user =>
                user.Email!.Contains(search)
                || user.FirstName.Contains(search)
                || user.LastName.Contains(search));
        }

        var totalCount = await query.CountAsync();
        var selectedUsers = await query
            .OrderBy(user => user.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = new List<UserResponse>();
        foreach (var user in selectedUsers)
        {
            items.Add(await CreateUserResponseAsync(user));
        }

        return new
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task<AuthSession> CreateSessionAsync(HotelUser user)
    {
        var userResponse = await CreateUserResponseAsync(user);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Role, userResponse.Role),
            new Claim("stamp", user.SecurityStamp ?? "")
        };

        var keyText = _configuration["Jwt:Key"]!;
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "VesperHotel",
            audience: _configuration["Jwt:Audience"] ?? "VesperHotel.Web",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        _db.RefreshSessions.Add(new RefreshSession
        {
            UserId = user.Id,
            TokenHash = BookingService.Hash(refreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        });

        return new AuthSession
        {
            RefreshToken = refreshToken,
            Response = new AuthResponse
            {
                User = userResponse,
                AccessToken = new JwtSecurityTokenHandler().WriteToken(jwt)
            }
        };
    }

    private async Task<UserResponse> CreateUserResponseAsync(HotelUser user)
    {
        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email ?? "",
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber ?? "",
            Role = isAdmin ? "Admin" : "Guest"
        };
    }
}
