using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using HttpMultipartParser;
using Konscious.Security.Cryptography;
using LiteDB;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRroomAPI.Database;

namespace VRroomAPI;
public static partial class HttpApi {
	private record struct RoutePattern(string Method, string Path);

    private static void AddRoute(string method, string path, Func<HttpListenerRequest, HttpListenerResponse, Task> handler) {
        Routes[new RoutePattern(method, path)] = handler;
    }
	
	private static Func<HttpListenerRequest, HttpListenerResponse, Task>? FindHandler(string method, string path) {
		path = path.Split('?')[0].TrimEnd('/');
		return (from route in Routes where route.Key.Method == method && MatchPath(route.Key.Path, path) select route.Value).FirstOrDefault();
	}

	private static bool MatchPath(string pattern, string path) {
		string[] patternParts = pattern.Split('/');
		string[] pathParts = path.Split('/');

		if (patternParts.Length != pathParts.Length) return false;
		return !patternParts.Where((t, i) => (!t.StartsWith("{") || !t.EndsWith("}")) && t != pathParts[i]).Any();
	}
	
	private static Dictionary<string, string> GetQueryParameters(HttpListenerRequest request) {
		Dictionary<string, string> parameters = new Dictionary<string, string>();
		if (request.Url?.Query == null) return parameters;
    
		string query = request.Url.Query.TrimStart('?');
    
		foreach (string param in query.Split('&', StringSplitOptions.RemoveEmptyEntries)) {
			string[] parts = param.Split('=');
			if (parts.Length == 2) {
				parameters[parts[0]] = HttpUtility.UrlDecode(parts[1]);
			}
		}
    
		return parameters;
	}
	
	private static string GenerateSecret(int length) {
		byte[] key = new byte[length];
		RandomNumberGenerator.Fill(key);
		return Convert.ToBase64String(key);
	}
	
	private static byte[] GenerateSalt() {
		byte[] key = new byte[16];
		RandomNumberGenerator.Fill(key);
		return key;
	}
	
	private static byte[] HashPassword(string password, byte[] salt) {
		var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password)) {
			Salt = salt,
			DegreeOfParallelism = 8,
			Iterations = 4,
			MemorySize = 1024 * 64
		};

		return argon2.GetBytes(32);
	}

	private static bool VerifyPassword(string password, byte[] hash, byte[] salt) {
		var newHash = HashPassword(password, salt);
		return hash.SequenceEqual(newHash);
	}
	
	private static Account? AuthenticateRequest(HttpListenerRequest request) {
		string? authHeader = request.Headers["Authorization"];
		if (string.IsNullOrEmpty(authHeader)) return null;
		string? handle = ValidateAuthToken(authHeader);
		return handle == null ? null : DatabaseAccess.Execute(db => db.GetCollection<Account>("accounts").FindOne(x => x.Handle == handle));
	}

	private static string? ValidateAuthToken(string token) {
		JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
		byte[] key = Convert.FromBase64String(SecretKey);
        
		try {
			TokenValidationParameters validationParameters = new TokenValidationParameters {
				ValidateIssuerSigningKey = true,
				IssuerSigningKey = new SymmetricSecurityKey(key),
				ValidateIssuer = false,
				ValidateAudience = false,
				ClockSkew = TimeSpan.Zero
			};

			ClaimsPrincipal? principal = tokenHandler.ValidateToken(token, validationParameters, out _);
			return principal.Identity?.Name;
		}
		catch {
			return null;
		}
	}

	private static bool TryAuthenticateRequest(HttpListenerRequest request, HttpListenerResponse response, out Account account) {
		Account? temp = AuthenticateRequest(request);
		account = temp!;
		if (temp != null) return false;
		response.StatusCode = 401;
		return true;
	}

	private static bool VerifyFields(HttpListenerResponse response, params object?[] args) {
		bool failed = false;
		foreach (object? o in args) {
			if (o == null) failed = true;
			if (o is string str && string.IsNullOrEmpty(str)) failed = true;
		}

		if (!failed) return false;
		response.StatusCode = 400;
		response.ContentType = "application/json";
		byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Missing required fields" }));
		response.OutputStream.Write(bytes);
		return true;
	}
	
	private static bool TryGetRequestJson(HttpListenerRequest request, HttpListenerResponse response, out JObject json) {
		try {
			using StreamReader reader = new StreamReader(request.InputStream);
			json = JObject.Parse(reader.ReadToEnd());
		}
		catch {
			json = null!;
			response.StatusCode = 400;
			response.ContentType = "application/json";
			byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = false, error = "Invalid request format" }));
			response.OutputStream.Write(bytes);
			return true;
		}

		return false;
	}
	
	private static string GenerateAuthToken(Account account) {
		SecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
		byte[] key = Convert.FromBase64String(SecretKey);
    
		SecurityTokenDescriptor tokenDescriptor = new SecurityTokenDescriptor {
			Subject = new ClaimsIdentity([
				new Claim(ClaimTypes.Name, account.Handle),
				new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
				new Claim("rank", account.Rank.ToString())
			]),
			Expires = DateTime.UtcNow.AddHours(24),
			SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
		};

		SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
		return tokenHandler.WriteToken(token);
	}

	private static async Task SendJsonResponse(HttpListenerResponse response, object data) {
		response.ContentType = "application/json";
		string json = JsonConvert.SerializeObject(data);
		byte[] bytes = Encoding.UTF8.GetBytes(json);
		await response.OutputStream.WriteAsync(bytes);
	}

	private static async Task SendErrorResponse(HttpListenerResponse response, string error) {
		response.StatusCode = 400;
		await SendJsonResponse(response, new { success = false, error });
	}
	
	private static bool IsValidHandle(string? handle) {
		return handle is { Length: >= 3 };
	}
	
	private static bool IsValidEmail(string? email) {
		if (email == null) return false;
		try {
			MailAddress addr = new MailAddress(email);
			return addr.Address == email;
		}
		catch {
			return false;
		}
	}

	private static bool IsValidPassword(string? password) {
		return password is { Length: >= 8 };
	}

	private static async Task<JObject?> GetRequestJson(HttpListenerRequest request) {
		using StreamReader reader = new StreamReader(request.InputStream);
		string json = await reader.ReadToEndAsync();
		try {
			return JObject.Parse(json);
		}
		catch {
			return null;
		}
	}
	
	private static async Task<MultipartFormDataParser?> GetMultipartFormData(HttpListenerRequest request) {
		if (!request.ContentType?.StartsWith("multipart/form-data") == true) return null;
        
		try {
			return await MultipartFormDataParser.ParseAsync(request.InputStream);
		}
		catch {
			return null;
		}
	}
	
	private static string GetContentType(string fileName) {
		return Path.GetExtension(fileName).ToLower() switch {
			".png" => "image/png",
			".jpg" => "image/jpeg",
			".jpeg" => "image/jpeg",
			".gif" => "image/gif",
			".webp" => "image/webp",
			_ => "application/octet-stream"
		};
	}
	
	private static void SaveToStorage(FilePart file, string id) {
		DatabaseAccess.Execute(db => {
			db.FileStorage.Upload(id, file.FileName, file.Data);
		});
	}
	
	private static object SearchUsers(ObjectId userId, string? searchName, int page, int limit) {
		return DatabaseAccess.Execute(db => {
			ILiteCollection<UserProfile> profiles = db.GetCollection<UserProfile>("profiles");
			ILiteCollection<BlockedUser> blocks = db.GetCollection<BlockedUser>("blocks");
        
			List<ObjectId> blockedIds = blocks.Find(b => b.UserId == userId || b.BlockedUserId == userId)
											  .SelectMany(b => new[] { b.UserId, b.BlockedUserId })
											  .Where(id => id != userId)
											  .ToList();

			IEnumerable<UserProfile> query = profiles.Find(p => p.Id != userId && !blockedIds.Contains(p.Id));
        
			if (!string.IsNullOrEmpty(searchName)) {
				string search = searchName.ToLowerInvariant();
				query = query.Where(p => p.Handle.ToLowerInvariant().Contains(search) || (p.DisplayName?.ToLowerInvariant().Contains(search) ?? false));
			}

			List<object> items = query.Skip(page * limit).Take(limit + 1).Select(p => new {
				userId = p.Id,
				handle = p.Handle,
				displayName = p.DisplayName,
				bio = p.Bio
			}).ToList<object>();

			bool hasMore = items.Count > limit;
			if (hasMore) items.RemoveAt(limit);
			
			return new { items, hasMore };
		});
	}
	
	private static object SearchContent(ObjectId userId, string contentType, string? searchName, string? filterTags, string? sortBy, int page, int limit) {
	    return DatabaseAccess.Execute(db => {
	        ILiteCollection<Content> contents = db.GetCollection<Content>("content");

	        IEnumerable<Content> query = contents.Find(c => c.ContentType == contentType && (c.IsPublic || c.OwnerId == userId || c.SharedWithUserIds.Contains(userId)));

	        if (!string.IsNullOrEmpty(searchName)) {
	            string search = searchName.ToLowerInvariant();
	            query = query.Where(c => c.Name.ToLowerInvariant().Contains(search));
	        }

	        if (!string.IsNullOrEmpty(filterTags)) {
	            List<string> tags = filterTags.Split(',').Select(t => t.Trim()).ToList();
	            query = query.Where(c => tags.All(t => c.ContentWarningTags.Contains(t)));
	        }

	        query = sortBy switch {
	            "newest" => query.OrderByDescending(c => c.CreatedAt),
	            "updated" => query.OrderByDescending(c => c.UpdatedAt),
	            "name" => query.OrderBy(c => c.Name),
	            _ => query.OrderByDescending(c => c.CreatedAt)
	        };

	        List<object> items = query.Skip(page * limit).Take(limit + 1).Select(c => new {
				id = c.Id,
				name = c.Name,
				description = c.Description,
				ownerId = c.OwnerId,
				isPublic = c.IsPublic,
				createdAt = c.CreatedAt,
				updatedAt = c.UpdatedAt,
				contentWarningTags = c.ContentWarningTags
			}).ToList<object>();

	        bool hasMore = items.Count > limit;
	        if (hasMore) items.RemoveAt(limit);

	        return new { items, hasMore };
	    });
	}
}