using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VRroomAPI.Models;
public class ShareGroup {
	[Key]
	public Guid Id { get; init; } = Guid.CreateVersion7();
	
	[ForeignKey("Owner")]
	public Guid OwnerId { get; set; }

	[MaxLength(32)]
	public string Name { get; set; } = "";

	public bool DefaultGroup { get; set; } = false;

	public List<UserProfile> SharedUsers { get; set; } = [];

	public UserProfile Owner { get; set; } = null!;
}