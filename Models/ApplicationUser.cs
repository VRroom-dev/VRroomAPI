using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using VRroomAPI.Models;

namespace VRroomAPI;
public class ApplicationUser : IdentityUser<Guid> {
	[Key]
	public override Guid Id { get; set; } = Guid.CreateVersion7();

	[NotMapped]
	public string? Handle {
		get => UserName;
		set => UserName = value!;
	}

	[NotMapped]
	public override string? PhoneNumber { get; set; }

	[NotMapped]
	public override bool PhoneNumberConfirmed { get; set; }

	public UserProfile? UserProfile { get; set; }
}