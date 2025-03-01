using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VRroomAPI.Interfaces;
using VRroomAPI.Migrations;
using VRroomAPI.Models;

namespace VRroomAPI.Controllers;
[ApiController, Route("v1/[controller]")]
public class ProfileController(
	ApplicationDbContext dbContext,
	IStorageProvider storageProvider)
	: ControllerBase {

	[HttpGet("{profileId}")]
	public async Task<IActionResult> GetProfile(string profileId) {
		if (!Guid.TryParse(profileId, out Guid id)) return BadRequest("Invalid profile GUID");

		UserProfile? profile = await dbContext.UserProfiles
			.Include(p => p.ApplicationUser)
			.FirstOrDefaultAsync(p => p.Id == id);

		if (profile == null) return NotFound("Profile not found");

		return Ok(new {
			guid = profile.Id,
			displayName = profile.DisplayName,
			status = profile.Status,
			bio = profile.Bio,
			isOnline = profile.IsOnline,
			availability = profile.Availability,
			createdAt = profile.CreatedAt,
			lastActiveAt = profile.LastActiveAt,
			handle = profile.ApplicationUser.Handle
		});
	}

	[HttpPut("Update"), Authorize]
	public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileModel model) {
		if (!ModelState.IsValid) return BadRequest(ModelState);
		
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

		UserProfile? profile = await dbContext.UserProfiles.FindAsync(userId);
		if (profile == null) return NotFound("Profile not found");

		if (model.DisplayName != null) profile.DisplayName = model.DisplayName;
		if (model.Status != null) profile.Status = model.Status;
		if (model.Bio != null) profile.Bio = model.Bio;
		if (model.Availability.HasValue) profile.Availability = model.Availability.Value;

		await dbContext.SaveChangesAsync();
		return Ok();
	}

	[HttpPut("UpdateThumbnail"), Authorize]
	public async Task<IActionResult> UpdateThumbnail() {
		string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		return Ok(await storageProvider.GetUploadUrl($"profiles/{userId}/thumbnail"));
	}

	[HttpPut("UpdateBanner"), Authorize]
	public async Task<IActionResult> UpdateBanner() {
		string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		return Ok(await storageProvider.GetUploadUrl($"profiles/{userId}/banner"));
	}
}