using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VRroomAPI.Interfaces;
using VRroomAPI.Migrations;
using VRroomAPI.Models;

namespace VRroomAPI.Controllers;
[ApiController, Route("v1/[controller]")]
public class SharesController(
	ApplicationDbContext dbContext,
	IStorageProvider storageProvider)
	: ControllerBase {

	[HttpGet("Groups"), Authorize]
	public IActionResult GetGroups() {
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();

		return Ok(dbContext.ShareGroups.Where(g => g.OwnerId == ownerId && g.DefaultGroup == false));
	}

	[HttpPost("Groups"), Authorize]
	public async Task<IActionResult> CreateGroup([FromBody] string name) {
		if (name.Length == 0) return BadRequest("A name is required");
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();

		ShareGroup group = new() {
			Name = name,
			OwnerId = ownerId
		};

		dbContext.ShareGroups.Add(group);
		await dbContext.SaveChangesAsync();

		return Ok();
	}

	[HttpDelete("Groups"), Authorize]
	public async Task<IActionResult> DeleteGroup([FromBody] string groupId) {
		if (!Guid.TryParse(groupId, out Guid id)) return BadRequest("Invalid group GUID");
		ShareGroup? group = await dbContext.ShareGroups.FirstOrDefaultAsync(c => c.Id == id);
		if (group == null) return NotFound("Group not found");
		
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();
		if (group.OwnerId != ownerId) return StatusCode(403, "Group is not owned by you");
		if (group.DefaultGroup) return StatusCode(403, "Cannot delete a default group");

		dbContext.ShareGroups.Remove(group);
		await dbContext.SaveChangesAsync();
		
		return Ok();
	}
	
	
	[HttpPut("Share"), Authorize]
	public async Task<IActionResult> AddShare([FromBody] ShareContentModel model) {
		if (!Guid.TryParse(model.GroupId, out Guid groupId)) return BadRequest("Invalid group GUID");
		ShareGroup? group = await dbContext.ShareGroups.FirstOrDefaultAsync(c => c.Id == groupId);
		if (group == null) return NotFound("Group not found");
		
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();
		if (group.OwnerId != ownerId) return StatusCode(403, "Group is not owned by you");
		
		if (!Guid.TryParse(model.UserId, out Guid userId)) return BadRequest("Invalid user GUID");
		UserProfile? user = await dbContext.UserProfiles.FirstOrDefaultAsync(u => u.Id == userId);
		if (user == null) return NotFound("User not found");

		group.SharedUsers.Add(user);
		await dbContext.SaveChangesAsync();

		return Ok();
	}

	[HttpDelete("Share"), Authorize]
	public async Task<IActionResult> RemoveShare([FromBody] ShareContentModel model) {
		if (!Guid.TryParse(model.GroupId, out Guid groupId)) return BadRequest("Invalid group GUID");
		ShareGroup? group = await dbContext.ShareGroups.FirstOrDefaultAsync(c => c.Id == groupId);
		if (group == null) return NotFound("Group not found");
		
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();
		if (group.OwnerId != ownerId) return StatusCode(403, "Group is not owned by you");
		
		if (!Guid.TryParse(model.UserId, out Guid userId)) return BadRequest("Invalid user GUID");
		UserProfile? user = await dbContext.UserProfiles.FirstOrDefaultAsync(u => u.Id == userId);
		if (user == null) return NotFound("User not found");

		group.SharedUsers.Remove(user);
		await dbContext.SaveChangesAsync();

		return Ok();
	}
}