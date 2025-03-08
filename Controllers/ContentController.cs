using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VRroomAPI.Interfaces;
using VRroomAPI.Migrations;
using VRroomAPI.Models;

namespace VRroomAPI.Controllers;
[ApiController, Route("v1/[controller]")]
public class ContentController(
	ApplicationDbContext dbContext,
	IStorageProvider storageProvider)
	: ControllerBase {

	[HttpGet("{contentId}")]
	public async Task<IActionResult> GetDetails(string contentId) {
		if (!Guid.TryParse(contentId, out Guid id)) return BadRequest("Invalid content GUID");

		Content? content = await dbContext.Content.FirstOrDefaultAsync(c => c.Id == id);
		if (content == null) return NotFound("Content not found");

		return Ok(new {
			content.ActiveBundleId,
			content.Name,
			content.Description,
			content.ContentType,
			content.ContentWarningTags,
			content.IsPublic,
			content.CreatedAt,
			content.UpdatedAt,
			content.PublicAt,
			content.OwnerId
		});
	}

	[HttpGet("Mine"), Authorize]
	public IActionResult GetContent() {
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

		IEnumerable<object> response = dbContext.Content
			.Where(c => c.OwnerId == userId)
			.Include(c => c.ActiveBundle)
			.Include(c => c.PreviousVersions)
			.Select(c => new {
				c.Id,
				c.Name,
				c.Description,
				c.ContentType,
				c.ContentWarningTags,
				c.IsPublic,
				c.CreatedAt,
				c.UpdatedAt,
				c.PublicAt,
				c.OwnerId,
				c.ActiveBundleId,
				PreviousVersions = c.PreviousVersions.Select(b => b.Id)
			});

		return Ok(response);
	}

	[HttpGet("{contentId}/Bundles"), Authorize]
	public async Task<IActionResult> GetBundles(string contentId) {
		if (!Guid.TryParse(contentId, out Guid id)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.Include(content => content.PreviousVersions).FirstOrDefaultAsync(c => c.Id == id);
		if (content == null) return NotFound("Content not found");
		
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();
		if (content.OwnerId != userId) return StatusCode(403, "Content is not owned by you");

		return Ok(content.PreviousVersions);
	}

	[HttpGet("{contentId}/Key"), Authorize]
	public async Task<IActionResult> GetKey(string contentId) {
		if (!Guid.TryParse(contentId, out Guid id)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.Include(content => content.ActiveBundle).FirstOrDefaultAsync(c => c.Id == id);
		if (content == null) return NotFound("Content not found");
		if (content.ActiveBundle == null) return Conflict("There is no active bundle attached to this content.");
		
		// check whether user should be able to access it here
		
		// if public always accessible
		// else if it exists in the instance the user is in its accessible
		// else if its shared to the user its accessible
		// else its inaccessible
		
		return Ok(content.ActiveBundle.DecryptionKey);
	}
	
	[HttpGet("{contentId}/ShareGroups"), Authorize]
	public async Task<IActionResult> GetGroups(string contentId) {
		if (!Guid.TryParse(contentId, out Guid id)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.Include(content => content.ShareGroups).FirstOrDefaultAsync(c => c.Id == id);
		if (content == null) return NotFound("Content not found");
		
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();
		if (content.OwnerId != ownerId) return StatusCode(403, "Content is not owned by you");

		return Ok(content.ShareGroups.Select(s => s.Id.ToString()));
	}
	
	[HttpPut("ShareGroups"), Authorize]
	public async Task<IActionResult> AddGroup([FromBody] ShareGroupModel model) {
		if (!Guid.TryParse(model.ContentId, out Guid contentId)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.Include(content => content.ShareGroups).FirstOrDefaultAsync(c => c.Id == contentId);
		if (content == null) return NotFound("Content not found");
		
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();
		if (content.OwnerId != ownerId) return StatusCode(403, "Content is not owned by you");
		
		if (!Guid.TryParse(model.GroupId, out Guid groupId)) return BadRequest("Invalid group GUID");
		ShareGroup? group = await dbContext.ShareGroups.FirstOrDefaultAsync(g => g.Id == groupId);
		if (group == null) return NotFound("Group not found");
		
		if (group.OwnerId != ownerId) return StatusCode(403, "Group is not owned by you");
		if (group.DefaultGroup) return StatusCode(403, "Cannot add a default group");
		
		content.ShareGroups.Add(group);
		await dbContext.SaveChangesAsync();

		return Ok();
	}
	
	[HttpDelete("ShareGroups"), Authorize]
	public async Task<IActionResult> RemoveGroup([FromBody] ShareGroupModel model) {
		if (!Guid.TryParse(model.ContentId, out Guid contentId)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.Include(content => content.ShareGroups).FirstOrDefaultAsync(c => c.Id == contentId);
		if (content == null) return NotFound("Content not found");
		
		string? ownerIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (ownerIdString == null || !Guid.TryParse(ownerIdString, out Guid ownerId)) return Unauthorized();
		if (content.OwnerId != ownerId) return StatusCode(403, "Content is not owned by you");
		
		if (!Guid.TryParse(model.GroupId, out Guid groupId)) return BadRequest("Invalid group GUID");
		ShareGroup? group = await dbContext.ShareGroups.FirstOrDefaultAsync(g => g.Id == groupId);
		if (group == null) return NotFound("Group not found");
		
		if (group.OwnerId != ownerId) return StatusCode(403, "Group is not owned by you");
		if (group.DefaultGroup) return StatusCode(403, "Cannot remove the default group");
		
		content.ShareGroups.Remove(group);
		await dbContext.SaveChangesAsync();

		return Ok();
	}

	[HttpPost("Create"), Authorize]
	public async Task<IActionResult> Create([FromBody] CreateContentModel model) {
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();

		ShareGroup defaultGroup = new() {
			Name = "",
			OwnerId = userId,
			DefaultGroup = true
		};
		
		Content content = new() {
			Name = model.Name,
			Description = model.Description,
			ContentType = model.ContentType,
			ContentWarningTags = model.ContentWarningTags,
			CreatedAt = DateTime.UtcNow,
			ShareGroups = [ defaultGroup ],
			OwnerId = userId
		};

		dbContext.ShareGroups.Add(defaultGroup);
		dbContext.Content.Add(content);
		await dbContext.SaveChangesAsync();

		return Ok(content.Id);
	}

	[HttpPut("Update"), Authorize]
	public async Task<IActionResult> Update([FromBody] UpdateContentModel model) {
		if (!Guid.TryParse(model.ContentId, out Guid contentId)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.FirstOrDefaultAsync(c => c.Id == contentId);
		if (content == null) return NotFound("Content not found");
		
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();
		if (content.OwnerId != userId) return StatusCode(403, "Content is not owned by you");

		if (model.Name != null) content.Name = model.Name;
		if (model.Description != null) content.Description = model.Description;
		if (model.ContentWarningTags.HasValue) content.ContentWarningTags = model.ContentWarningTags.Value;
		content.UpdatedAt = DateTime.UtcNow;
		await dbContext.SaveChangesAsync();
		
		return Ok();
	}
	
	[HttpPut("UpdateBundle"), Authorize]
	public async Task<IActionResult> UpdateBundle([FromBody] UpdateBundleModel model) {
		if (!Guid.TryParse(model.ContentId, out Guid contentId)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.Include(content => content.ActiveBundle).FirstOrDefaultAsync(c => c.Id == contentId);
		if (content == null) return NotFound("Content not found");
		
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();
		if (content.OwnerId != userId) return StatusCode(403, "Content is not owned by you");

		Bundle bundle = new() {
			ContentId = contentId,
			DecryptionKey = model.DecryptionKey,
			Version = (content.ActiveBundle?.Version ?? 0) + 1,
			CreatedAt = DateTime.UtcNow,
		};

		dbContext.Bundles.Add(bundle);
		await dbContext.SaveChangesAsync();
		
		content.UpdatedAt = DateTime.UtcNow;
		content.ActiveBundleId = bundle.Id;
		content.PreviousVersions.Add(bundle);
		await dbContext.SaveChangesAsync();
		
		return Ok(await storageProvider.GetUploadUrl($"content/{model.ContentId}/{bundle.Id}"));
	}
	
	[HttpPut("UpdateThumbnail"), Authorize]
	public async Task<IActionResult> UpdateThumbnail([FromBody] BaseContentModel model) {
		if (!Guid.TryParse(model.ContentId, out Guid contentId)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.FirstOrDefaultAsync(c => c.Id == contentId);
		if (content == null) return NotFound("Content not found");
		
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();
		if (content.OwnerId != userId) return StatusCode(403, "Content is not owned by you");
		
		content.UpdatedAt = DateTime.UtcNow;
		await dbContext.SaveChangesAsync();
		
		return Ok(await storageProvider.GetUploadUrl($"content/{model.ContentId}/thumbnail"));
	}

	[HttpPut("SetActiveBundle"), Authorize]
	public async Task<IActionResult> SetActiveBundle([FromBody] SetActiveBundleModel model) {
		if (!Guid.TryParse(model.ContentId, out Guid contentId)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.FirstOrDefaultAsync(c => c.Id == contentId);
		if (content == null) return NotFound("Content not found");
		
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();
		if (content.OwnerId != userId) return StatusCode(403, "Content is not owned by you");
		
		if (!Guid.TryParse(model.BundleId, out Guid bundleId)) return BadRequest("Invalid bundle GUID");
		Bundle? bundle = await dbContext.Bundles.FirstOrDefaultAsync(c => c.Id == bundleId);
		if (bundle == null) return NotFound("Bundle not found");
		
		if (bundle.ContentId != content.Id) return StatusCode(403, "Bundle does not belong to this content");
		
		content.ActiveBundleId = bundle.Id;
		await dbContext.SaveChangesAsync();

		return Ok();
	}

	[HttpDelete("Delete"), Authorize]
	public async Task<IActionResult> DeleteContent([FromBody] BaseContentModel model) {
		if (!Guid.TryParse(model.ContentId, out Guid contentId)) return BadRequest("Invalid content GUID");
		Content? content = await dbContext.Content.Include(content => content.ShareGroups).Include(content => content.PreviousVersions).FirstOrDefaultAsync(c => c.Id == contentId);
		if (content == null) return NotFound("Content not found");
		
		string? userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (userIdString == null || !Guid.TryParse(userIdString, out Guid userId)) return Unauthorized();
		if (content.OwnerId != userId) return StatusCode(403, "Content is not owned by you");

		ShareGroup defaultGroup = content.ShareGroups.Find(g => g.DefaultGroup)!;

		foreach (Bundle bundle in content.PreviousVersions) {
			await storageProvider.DeleteObject($"content/{content.Id}/{bundle.Id}");
		}

		content.ActiveBundle = null;
		await dbContext.SaveChangesAsync();
		
		dbContext.Remove(defaultGroup);
		dbContext.Content.Remove(content);
		await dbContext.SaveChangesAsync();

		return Ok();
	}
}