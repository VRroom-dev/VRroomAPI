using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using VRroomAPI.Migrations;
using VRroomAPI.Models;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace VRroomAPI.Controllers;
[ApiController, Route("v1/[controller]")]
public class AuthController(
	UserManager<ApplicationUser> userManager,
	SignInManager<ApplicationUser> signInManager,
	ApplicationDbContext dbContext,
	IConfiguration configuration)
	: ControllerBase {
	
	[HttpPost("Register")]
	public async Task<IActionResult> Register([FromBody] RegisterModel model) {
		if (!ModelState.IsValid) return BadRequest(ModelState);

		ApplicationUser user = new() {
			Handle = model.Handle, 
			Email = model.Email
		};
		
		IdentityResult result = await userManager.CreateAsync(user, model.Password);

		if (result.Succeeded) {
			UserProfile profile = new() {
				Id = user.Id,
				DisplayName = model.Handle,
				CreatedAt = DateTime.UtcNow
			};

			dbContext.UserProfiles.Add(profile);
			await dbContext.SaveChangesAsync();
			
			return Ok("User registered successfully.");
		}

		foreach (IdentityError error in result.Errors) ModelState.AddModelError("", error.Description);
		return BadRequest(ModelState);
	}

	[HttpPost("Login")]
    public async Task<IActionResult> Login([FromBody] LoginModel model) {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        ApplicationUser? user;
        if (model.Identifier.Contains("@")) user = await userManager.FindByEmailAsync(model.Identifier);
        else user = await userManager.FindByNameAsync(model.Identifier);
        if (user == null) return Unauthorized("Invalid login attempt.");

        SignInResult signInResult = await signInManager.PasswordSignInAsync(user, model.Password, false, false);
        if (!signInResult.Succeeded) return Unauthorized("Invalid login attempt.");

        string token = await GenerateJwtTokenAndStoreSession(user);
        return Ok(new { token });
    }

    [HttpGet("Sessions"), Authorize]
    public IActionResult GetSessions() {
		string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null || !Guid.TryParse(userId, out Guid guid)) return Unauthorized();

        List<UserSession> sessions = dbContext.UserSessions.Where(s => s.UserId == guid).ToList();
        return Ok(sessions);
    }
	
	[HttpGet("Test"), Authorize]
	public IActionResult TestAuthorization() => Ok();

	private async Task<string> GenerateJwtTokenAndStoreSession(ApplicationUser user) {
		Guid jti = Guid.NewGuid();
		List<Claim> claims = new List<Claim> {
			new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
			new(JwtRegisteredClaimNames.Jti, jti.ToString())
		};

		SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
		SigningCredentials credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

		JwtSecurityToken token = new JwtSecurityToken(
			issuer: configuration["Jwt:Issuer"],
			audience: configuration["Jwt:Audience"],
			claims: claims,
			expires: DateTime.Now.AddDays(30),
			signingCredentials: credentials
		);

		string? tokenString = new JwtSecurityTokenHandler().WriteToken(token);

		UserSession userSession = new UserSession {
			UserId = user.Id,
			JwtId = jti,
			CreatedAt = DateTime.UtcNow,
			ExpiresAt = token.ValidTo
		};

		dbContext.UserSessions.Add(userSession);
		await dbContext.SaveChangesAsync();

		return tokenString;
	}
}