namespace VRroomAPI.Interfaces;

public interface IStorageProvider {
	public Task<string> GetUploadUrl(string fileKey);
}