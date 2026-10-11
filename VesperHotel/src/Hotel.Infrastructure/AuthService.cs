using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Hotel.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Hotel.Infrastructure;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(254)] public string Email { get; set; } = "";
    [Required, MinLength(10), MaxLength(128)] public string Password { get; set; } = "";
    [Required, MaxLength(80)] public string FirstName { get; set; } = "";
    [Required, MaxLength(80)] public string LastName { get; set; } = "";
    [MaxLength(40)] public string PhoneNumber { get; set; } = "";
}
public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, MaxLength(128)] public string Password { get; set; } = "";
}
public class ProfileRequest
{
    [Required, MaxLength(80)] public string FirstName { get; set; } = "";
    [Required, MaxLength(80)] public string LastName { get; set; } = "";
    [MaxLength(40)] public string PhoneNumber { get; set; } = "";
}
public class ForgotPasswordRequest { [Required, EmailAddress] public string Email { get; set; } = ""; }
public class ResetPasswordRequest
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Token { get; set; } = "";
    [Required, MinLength(10), MaxLength(128)] public string NewPassword { get; set; } = "";
}
public class UserResponse
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Role { get; set; } = "Guest";
}
public class AuthResponse
{
    public string AccessToken { get; set; } = "";
    public UserResponse User { get; set; } = new();
}
public class AuthSession
{
    public AuthResponse Response { get; set; } = new();
    public string RefreshToken { get; set; } = "";
}

public class AuthService
{
    private readonly HotelDbContext db;
    private readonly UserManager<HotelUser> users;
    private readonly SignInManager<HotelUser> signIn;
    private readonly IConfiguration config;
    public AuthService(HotelDbContext db, UserManager<HotelUser> users, SignInManager<HotelUser> signIn, IConfiguration config)
    { this.db = db; this.users = users; this.signIn = signIn; this.config = config; }

    public async Task<AuthSession> RegisterAsync(RegisterRequest request)
    {
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var user = new HotelUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), PhoneNumber = request.PhoneNumber.Trim() };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) throw new HotelException(400, "registration_failed", string.Join(" ", result.Errors.Select(x => x.Description)));
        await users.AddToRoleAsync(user, "Guest");
        var session = await IssueAsync(user);
        await db.SaveChangesAsync(); await write.CommitAsync(); return session;
    }
    public async Task<AuthSession> LoginAsync(LoginRequest request)
    {
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user == null) throw new HotelException(401, "invalid_credentials", "Email or password is incorrect.");
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            await write.CommitAsync();
            throw new HotelException(401, "invalid_credentials", result.IsLockedOut ? "Too many attempts. Try again in 15 minutes." : "Email or password is incorrect.");
        }
        var session = await IssueAsync(user);
        await db.SaveChangesAsync(); await write.CommitAsync(); return session;
    }
    public async Task<AuthSession> RefreshAsync(string? token)
    {
        if (string.IsNullOrEmpty(token)) throw new HotelException(401, "session_expired", "Please sign in again.");
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var hash = BookingService.Hash(token);
        var stored = await db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (stored == null || stored.RevokedAtUtc != null || stored.ExpiresAtUtc <= DateTime.UtcNow)
            throw new HotelException(401, "session_expired", "Please sign in again.");
        var user = await users.FindByIdAsync(stored.UserId) ?? throw new HotelException(401, "session_expired", "Please sign in again.");
        stored.RevokedAtUtc = DateTime.UtcNow;
        var session = await IssueAsync(user);
        await db.SaveChangesAsync(); await write.CommitAsync(); return session;
    }
    public async Task LogoutAsync(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var hash = BookingService.Hash(token);
        var stored = await db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (stored != null) stored.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(); await write.CommitAsync();
    }
    public async Task<UserResponse> MeAsync(string id)
    {
        var user = await users.FindByIdAsync(id) ?? throw new HotelException(401, "user_not_found", "Please sign in again.");
        return await MapAsync(user);
    }
    public async Task<UserResponse> ProfileAsync(string id, ProfileRequest request)
    {
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var user = await users.FindByIdAsync(id) ?? throw new HotelException(401, "user_not_found", "Please sign in again.");
        user.FirstName = request.FirstName.Trim(); user.LastName = request.LastName.Trim(); user.PhoneNumber = request.PhoneNumber.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) throw new HotelException(400, "profile_failed", "The profile could not be saved.");
        await write.CommitAsync(); return await MapAsync(user);
    }
    public async Task ForgotAsync(string email)
    {
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var user = await users.FindByEmailAsync(email.Trim());
        if (user != null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var url = (config["Frontend:Url"] ?? "http://localhost:5173").TrimEnd('/') + "/reset-password?email=" + Uri.EscapeDataString(email.Trim()) + "&token=" + Uri.EscapeDataString(token);
            db.Outbox.Add(new OutboxEvent { Type = "PasswordReset", UserId = user.Id,
                PayloadJson = JsonDefaults.Serialize(new NotificationMessage { Subject = "Reset your VESPER password", Message = "Use this private link within one hour: " + url }) });
            await db.SaveChangesAsync();
        }
        await write.CommitAsync();
    }
    public async Task ResetAsync(ResetPasswordRequest request)
    {
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user == null) throw new HotelException(400, "invalid_reset", "The reset link is invalid or expired.");
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded) throw new HotelException(400, "invalid_reset", "The reset link is invalid, expired, or the password does not meet requirements.");
        var sessions = await db.RefreshSessions.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null).ToListAsync();
        foreach (var session in sessions) session.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(); await write.CommitAsync();
    }
    public async Task<object> ListUsersAsync(int page, int pageSize, string? search)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Email!.Contains(search) || x.FirstName.Contains(search) || x.LastName.Contains(search));
        var count = await query.CountAsync();
        var selected = await query.OrderBy(x => x.Email).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = new List<UserResponse>();
        foreach (var user in selected) items.Add(await MapAsync(user));
        return new { Items = items, TotalCount = count, Page = page, PageSize = pageSize };
    }
    private async Task<AuthSession> IssueAsync(HotelUser user)
    {
        var response = await MapAsync(user);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Email, user.Email!), new(ClaimTypes.Role, response.Role), new("stamp", user.SecurityStamp ?? "") };
        var jwt = new JwtSecurityToken(config["Jwt:Issuer"] ?? "VesperHotel", config["Jwt:Audience"] ?? "VesperHotel.Web", claims,
            expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        db.RefreshSessions.Add(new RefreshSession { UserId = user.Id, TokenHash = BookingService.Hash(refresh), ExpiresAtUtc = DateTime.UtcNow.AddDays(7) });
        return new AuthSession { Response = new AuthResponse { User = response, AccessToken = new JwtSecurityTokenHandler().WriteToken(jwt) }, RefreshToken = refresh };
    }
    private async Task<UserResponse> MapAsync(HotelUser user) => new()
    { Id = user.Id, Email = user.Email ?? "", FirstName = user.FirstName, LastName = user.LastName, PhoneNumber = user.PhoneNumber ?? "", Role = await users.IsInRoleAsync(user, "Admin") ? "Admin" : "Guest" };
}
