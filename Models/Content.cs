using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VRroomAPI.Enums;

namespace VRroomAPI.Models;
public class Content {
	[Key]
	public Guid Id { get; init; } = Guid.CreateVersion7();
	
	[ForeignKey("Owner")]
	public Guid OwnerId { get; set; }
	
	[ForeignKey("ActiveBundle")]
	public Guid? ActiveBundleId { get; set; }
	
	[MaxLength(32)]
	public string Name { get; set; } = "";

	[MaxLength(256)]
	public string Description { get; set; } = "";
	
	public ContentType ContentType { get; set; }
	
	public ContentWarningTags ContentWarningTags { get; set; }

	public bool IsPublic { get; set; }
	
	public DateTime CreatedAt { get; set; }
	
	public DateTime UpdatedAt { get; set; }
	
	public DateTime PublicAt { get; set; }

	public List<ShareGroup> ShareGroups { get; set; } = null!;

	public List<Bundle> PreviousVersions { get; set; } = [];
	
	public Bundle? ActiveBundle { get; set; } = null!;
	
	public UserProfile Owner { get; set; } = null!;
}