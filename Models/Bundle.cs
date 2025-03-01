using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VRroomAPI.Models;
public class Bundle {
	[Key]
	public Guid Id { get; init; } = Guid.CreateVersion7();
	
	[ForeignKey("Content")]
	public Guid ContentId { get; set; }
	
	[MaxLength(44)]
	public string DecryptionKey { get; set; } = null!;
	
	public int Version { get; set; }
	
	public DateTime CreatedAt { get; set; }

	public Content Content { get; set; } = null!;
}