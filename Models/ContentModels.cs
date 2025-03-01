using VRroomAPI.Enums;

namespace VRroomAPI.Models;
public class UpdateBundleModel {
	public required string ContentId { get; set; }
	public required string DecryptionKey { get; set; }
}

public class SetActiveBundleModel {
	public required string ContentId { get; set; }
	public required string BundleId { get; set; }
}

public class ShareGroupModel {
	public required string ContentId { get; set; }
	public required string GroupId { get; set; }
}

public class UpdateContentModel {
	public required string ContentId { get; set; }
	public string? Name { get; set; }
	public string? Description { get; set; }
	public ContentWarningTags? ContentWarningTags { get; set; }
}

public class CreateContentModel {
	public required string Name { get; set; }
	public required string Description { get; set; }
	public required ContentType ContentType { get; set; }
	public required ContentWarningTags ContentWarningTags { get; set; }
}