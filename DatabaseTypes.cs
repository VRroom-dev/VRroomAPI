using LiteDB;

namespace VRroomAPI.Database;
public class Account {
	public ObjectId Id { get; set; } = null!;
	public string Handle { get; set; } = "";
	public string Email { get; set; } = "";
	public byte[] PasswordHash { get; set; } = [];
	public byte[] PasswordSalt { get; set; } = [];
	public bool IsVerified { get; set; }
	public string? VerificationCode { get; set; }
	public Rank Rank { get; set; } = Rank.User;
	public string GameToken { get; set; } = "";
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}

public class UserProfile {
	public ObjectId Id { get; set; } = null!;
	public string Handle { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Bio { get; set; } = "";
	public string Status { get; set; } = "";
	public bool DoNotDisturb { get; set; }
	public string? Location { get; set; } = "";
	public bool ShowRank { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
	public DateTime LastActivity { get; set; }
}

public class Session {
	public ObjectId Id { get; set; } = null!;
	public ObjectId UserId { get; set; } = null!;
	public string DeviceInfo { get; set; } = "";
	public DateTime CreatedAt { get; set; }
	public DateTime LastUsedAt { get; set; }
}

public class JoinToken {
	public ObjectId Id { get; set; } = null!;
	public ObjectId UserId { get; set; } = null!;
	public string Token { get; set; } = "";
	public DateTime ExpiresAt { get; set; }
}

public class FriendRequest {
	public ObjectId Id { get; set; } = null!;
	public ObjectId FromUserId { get; set; } = null!;
	public ObjectId ToUserId { get; set; } = null!;
	public DateTime CreatedAt { get; set; }
}

public class Friendship {
	public ObjectId Id { get; set; } = null!;
	public ObjectId User1Id { get; set; } = null!;
	public ObjectId User2Id { get; set; } = null!;
	public DateTime CreatedAt { get; set; }
}

public class BlockedUser {
	public ObjectId Id { get; set; } = null!;
	public ObjectId UserId { get; set; } = null!;
	public ObjectId BlockedUserId { get; set; } = null!;
	public DateTime CreatedAt { get; set; }
}

public class Notification {
	public ObjectId Id { get; set; } = null!;
	public ObjectId UserId { get; set; } = null!;
	public string SenderType { get; set; } = "";
	public string SenderId { get; set; } = "";
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public string? AttachmentIds { get; set; }
	public DateTime CreatedAt { get; set; }
}

public class Content {
	public Guid Id { get; set; }
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";
	public ObjectId OwnerId { get; set; } = null!;
	public bool IsPublic { get; set; }
	public List<ObjectId> SharedWithUserIds { get; set; } = new();
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
	public List<string> ContentWarningTags { get; set; } = new();
	public string ContentType { get; set; } = "";
}

public class Ticket {
	public ObjectId Id { get; set; } = null!;
	public ObjectId UserId { get; set; } = null!;
	public string Type { get; set; } = "";
	public string Title { get; set; } = "";
	public string Status { get; set; } = "";
	public Guid? ContentId { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}

public class TicketMessage {
	public ObjectId Id { get; set; } = null!;
	public ObjectId TicketId { get; set; } = null!;
	public ObjectId SenderId { get; set; } = null!;
	public bool IsStaff { get; set; }
	public string Message { get; set; } = "";
	public List<string> AttachmentIds { get; set; } = [];
	public DateTime CreatedAt { get; set; }
}

public enum Rank {
	User = 0,
	Moderator = 1,
	Admin = 2,
	Developer = 3
}