namespace VRroomAPI.Models;

public class RegisterModel {
	public required string Handle { get; set; }
	public required string Email { get; set; }
	public required string Password { get; set; }
}

public class LoginModel {
	public required string Identifier { get; set; }
	public required string Password { get; set; }
}