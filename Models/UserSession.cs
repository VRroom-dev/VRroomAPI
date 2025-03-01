namespace VRroomAPI.Models;

public class UserSession {
	public int Id { get; set; }
	public Guid UserId { get; set; }
	public Guid JwtId { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime ExpiresAt { get; set; }
}