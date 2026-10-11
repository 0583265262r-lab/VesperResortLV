using System.ComponentModel.DataAnnotations;

namespace Hotel.Api.Models;

public class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public string Email { get; set; } = "";

    [Required]
    [MinLength(10)]
    [MaxLength(128)]
    public string Password { get; set; } = "";

    [Required]
    [MaxLength(80)]
    public string FirstName { get; set; } = "";

    [Required]
    [MaxLength(80)]
    public string LastName { get; set; } = "";

    [MaxLength(40)]
    public string PhoneNumber { get; set; } = "";
}

public class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [MaxLength(128)]
    public string Password { get; set; } = "";
}

public class ProfileRequest
{
    [Required]
    [MaxLength(80)]
    public string FirstName { get; set; } = "";

    [Required]
    [MaxLength(80)]
    public string LastName { get; set; } = "";

    [MaxLength(40)]
    public string PhoneNumber { get; set; } = "";
}

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";
}

public class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    [Required]
    [MinLength(10)]
    [MaxLength(128)]
    public string NewPassword { get; set; } = "";
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
    public UserResponse User { get; set; } = new UserResponse();
}

public class AuthSession
{
    public AuthResponse Response { get; set; } = new AuthResponse();
    public string RefreshToken { get; set; } = "";
}
