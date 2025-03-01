using Amazon.S3;
using Amazon.S3.Model;
using VRroomAPI.Interfaces;

namespace VRroomAPI.Services;
public class S3Storage : IStorageProvider {
	private static readonly AmazonS3Client Client;
	private static readonly string BucketName;

	static S3Storage() {
		BucketName = Environment.GetEnvironmentVariable("S3BucketName")!;
		string endpoint = Environment.GetEnvironmentVariable("S3Endpoint")!;
		string accessKeyId = Environment.GetEnvironmentVariable("S3AccessKeyId")!;
		string secretAccessKey = Environment.GetEnvironmentVariable("S3SecretAccessKey")!;
		
		AmazonS3Config config = new() { ServiceURL = endpoint };
		Client = new(accessKeyId, secretAccessKey, config);
	}
	
	public async Task<string> GetUploadUrl(string fileKey) {
		GetPreSignedUrlRequest request = new() {
			BucketName = BucketName,
			Key = fileKey,
			Verb = HttpVerb.PUT,
			Expires = DateTime.UtcNow.AddMinutes(5)
		};
		
		return await Client.GetPreSignedURLAsync(request);
	}
}