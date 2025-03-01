using VRroomAPI.Enums;

namespace VRroomAPI.Models;

public class UpdateProfileModel {
	public string? DisplayName { get; set; }
	public string? Status { get; set; }
	public string? Bio { get; set; }
	public Availability? Availability { get; set; }
}