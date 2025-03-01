using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VRroomAPI.Enums;

namespace VRroomAPI.Models;
public class UserProfile {
	[Key, ForeignKey("ApplicationUser")]
	public Guid Id { get; set; }
	
	[MaxLength(32)]
	public required string DisplayName { get; set; }

	[MaxLength(32)]
	public string Status { get; set; } = "";

	[MaxLength(256)]
	public string Bio { get; set; } = "";

	//public Location Location { get; set; }
	
	public bool IsOnline { get; set; }
	
	public Availability Availability { get; set; }
	
	public DateTime CreatedAt { get; set; }
	
	public DateTime LastActiveAt { get; set; }
	
	public virtual List<Content> OwnedContent { get; set; } = [];
	
	public ApplicationUser ApplicationUser { get; set; } = null!;
}