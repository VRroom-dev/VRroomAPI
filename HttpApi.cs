using System.Net;
using HttpMultipartParser;
using LiteDB;
using Newtonsoft.Json.Linq;
using VRroomAPI.Database;

namespace VRroomAPI;
public static partial class HttpApi {
	private static readonly HttpListener Listener = new();
	private static readonly Dictionary<RoutePattern, Func<HttpListenerRequest, HttpListenerResponse, Task>> Routes = new();
	private static readonly string SecretKey = GenerateSecret(32);

	public static void Start() {
		SetupRoutes();
		Listener.Prefixes.Add("http://*:8080/");
		Listener.Start();
		Task.Run(HandleRequests);
	}
	
    private static void SetupRoutes() {
        // Account routes
        AddRoute("POST", "/account", HandleCreateAccount);
        AddRoute("POST", "/account/verify", HandleVerifyAccount);
        AddRoute("POST", "/account/verify/resend", HandleResendVerification);
        AddRoute("PUT", "/account", HandleUpdateAccount);
        AddRoute("PATCH", "/account/handle", HandleUpdateHandle);
        AddRoute("PATCH", "/account/email", HandleUpdateEmail);
        AddRoute("PATCH", "/account/password", HandleUpdatePassword);
        AddRoute("GET", "/account/friend-requests", HandleGetFriendRequests);
        AddRoute("GET", "/account/friends", HandleGetFriends);
        AddRoute("GET", "/account/notifications", HandleGetNotifications);
        AddRoute("DELETE", "/account", HandleDeleteAccount);

        // Auth routes
        AddRoute("POST", "/auth/login", HandleLogin);
        AddRoute("GET", "/auth/sessions", HandleGetSessions);
        AddRoute("DELETE", "/auth/sessions", HandleDeleteAllSessions);
        AddRoute("DELETE", "/auth/sessions/{id}", HandleDeleteSession);
        AddRoute("GET", "/auth/game-token", HandleGetGameToken);
        AddRoute("POST", "/auth/game-token", HandleRegenerateGameToken);
        AddRoute("GET", "/auth/join-token", HandleGetJoinToken);
        AddRoute("POST", "/auth/join-token", HandleVerifyJoinToken);

        // User routes
        AddRoute("GET", "/user/{id}", HandleGetUser);
        AddRoute("GET", "/users", HandleGetUsers);
        AddRoute("POST", "/user/{id}/friend", HandleAddFriend);
        AddRoute("DELETE", "/user/{id}/friend", HandleRemoveFriend);
        AddRoute("POST", "/user/{id}/block", HandleBlockUser);

        // Misc routes
        AddRoute("GET", "/search", HandleSearch);
        AddRoute("GET", "/image/{id}", HandleGetImage);

        // Content routes
        AddRoute("POST", "/content", HandleCreateContent);
        AddRoute("PUT", "/content/{id}", HandleUpdateContent);
        AddRoute("PUT", "/content/{id}/share", HandleShareContent);
        AddRoute("GET", "/content", HandleGetAllContent);
        AddRoute("GET", "/content/{id}", HandleGetContent);
        AddRoute("GET", "/content/{id}/download", HandleDownloadContent);
        AddRoute("DELETE", "/content/{id}", HandleDeleteContent);
    }

    private static async Task HandleRequests() {
		while (Listener.IsListening) {
			HttpListenerContext context = await Listener.GetContextAsync();
			_ = ProcessRequestAsync(context);
		}
	}
	
	private static async Task ProcessRequestAsync(HttpListenerContext context) {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        
		try {
			Func<HttpListenerRequest, HttpListenerResponse, Task>? handler = FindHandler(request.HttpMethod, request.Url?.LocalPath ?? "");
			if (handler != null) await handler(request, response);
            else {
                await SendErrorResponse(response, "Endpoint not found");
                response.StatusCode = 404;
            }
		}
		catch (Exception) {
            await SendErrorResponse(response, "Internal server error");
		}
		finally {
			context.Response.Close();
		}
	}

    #region Account
    
    private static async Task HandleCreateAccount(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryGetRequestJson(request, response, out JObject json)) return;

        string? handle = json["handle"]?.ToString();
        string? email = json["email"]?.ToString();
        string? password = json["password"]?.ToString();

        if (VerifyFields(response, handle, email, password)) return;

        (bool, string) result = DatabaseAccess.Execute(db => {
            ILiteCollection<Account>? accounts = db.GetCollection<Account>("accounts");
            
            if (accounts.Exists(a => a.Handle == handle)) return (false, "Handle already taken");
            if (accounts.Exists(a => a.Email == email)) return (false, "Email already registered");

            byte[] salt = GenerateSalt();
            Account account = new Account {
                Handle = handle!,
                Email = email!,
                PasswordHash = HashPassword(password!, salt),
                PasswordSalt = salt,
                IsVerified = false,
                VerificationCode = GenerateSecret(32),
                GameToken = GenerateSecret(32),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            
            accounts.Insert(account);

            ILiteCollection<UserProfile>? profiles = db.GetCollection<UserProfile>("profiles");
            UserProfile profile = new UserProfile {
                Id = account.Id,
                Handle = handle!,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            profiles.Insert(profile);

            return (true, "");
        });

        if (!result.Item1) {
            await SendErrorResponse(response, result.Item2);
            return;
        }

        await SendJsonResponse(response, new { success = true });
    }

    private static async Task HandleVerifyAccount(HttpListenerRequest request, HttpListenerResponse response) {
        string? code = request.QueryString["code"];
        if (VerifyFields(response, code)) return;

        bool result = DatabaseAccess.Execute(db => {
            ILiteCollection<Account>? accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindOne(a => a.VerificationCode == code);
        
            if (account == null) return false;

            account.IsVerified = true;
            account.VerificationCode = null;
            account.UpdatedAt = DateTime.UtcNow;
        
            accounts.Update(account);
            return true;
        });

        await SendJsonResponse(response, new { success = result });
    }

    private static async Task HandleResendVerification(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        bool result = DatabaseAccess.Execute(db => {
            ILiteCollection<Account>? accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindById(user.Id);
        
            if (account == null || account.IsVerified) return false;

            account.VerificationCode = GenerateSecret(32);
            account.UpdatedAt = DateTime.UtcNow;
        
            accounts.Update(account);
            return true;
        });

        await SendJsonResponse(response, new { success = result });
    }

    private static async Task HandleUpdateAccount(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        MultipartFormDataParser? formData = await GetMultipartFormData(request);
        if (formData == null) {
            await SendErrorResponse(response, "Invalid request format");
            return;
        }

        foreach (FilePart? file in formData.Files) {
            if (file.Name == "thumbnail") {
                DatabaseAccess.Execute(db => {
                    db.FileStorage.Upload($"usr_{user.Handle}-thumbnail", file.FileName, file.Data);
                });
            }
            else if (file.Name == "banner") {
                DatabaseAccess.Execute(db => {
                    db.FileStorage.Upload($"usr_{user.Handle}-banner", file.FileName, file.Data);
                });
            }
        }

        string? updatesJson = formData.GetParameterValue("updates");
        if (string.IsNullOrEmpty(updatesJson)) {
            await SendErrorResponse(response, "No updates provided");
            return;
        }

        JObject updates = JObject.Parse(updatesJson);

        bool result = DatabaseAccess.Execute(db => {
            ILiteCollection<UserProfile>? profiles = db.GetCollection<UserProfile>("profiles");
            UserProfile? profile = profiles.FindById(user.Id);
        
            if (profile == null)
                return false;

            if (updates["displayName"] != null)
                profile.DisplayName = updates["displayName"]!.ToString();
            if (updates["bio"] != null)
                profile.Bio = updates["bio"]!.ToString();
            if (updates["status"] != null)
                profile.Status = updates["status"]!.ToString();

            profile.UpdatedAt = DateTime.UtcNow;
        
            profiles.Update(profile);
            return true;
        });

        await SendJsonResponse(response, new { success = result });
    }

    private static async Task HandleUpdateHandle(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        JObject? json = await GetRequestJson(request);
        if (json == null) {
            await SendErrorResponse(response, "Invalid request format");
            return;
        }

        string? newHandle = json["handle"]?.ToString();
        if (IsValidHandle(newHandle)) {
            await SendErrorResponse(response, "Invalid handle");
            return;
        }

        (bool, string) result = DatabaseAccess.Execute(db => {
            ILiteCollection<Account>? accounts = db.GetCollection<Account>("accounts");
            ILiteCollection<UserProfile>? profiles = db.GetCollection<UserProfile>("profiles");

            if (accounts.Exists(a => a.Handle == newHandle && a.Id != user.Id))
                return (false, "Handle already taken");

            Account? account = accounts.FindById(user.Id);
            UserProfile? profile = profiles.FindById(user.Id);
        
            if (account == null || profile == null)
                return (false, "Account not found");

            account.Handle = newHandle!;
            account.UpdatedAt = DateTime.UtcNow;
            accounts.Update(account);

            profile.Handle = newHandle!;
            profile.UpdatedAt = DateTime.UtcNow;
            profiles.Update(profile);

            return (true, "");
        });

        if (!result.Item1) {
            await SendErrorResponse(response, result.Item2);
            return;
        }

        await SendJsonResponse(response, new { success = true });
    }

    private static async Task HandleUpdateEmail(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        JObject? json = await GetRequestJson(request);
        if (json == null) {
            await SendErrorResponse(response, "Invalid request format");
            return;
        }

        string? newEmail = json["email"]?.ToString();
        if (!IsValidEmail(newEmail)) {
            await SendErrorResponse(response, "Invalid email");
            return;
        }

        (bool, string) result = DatabaseAccess.Execute(db => {
            ILiteCollection<Account>? accounts = db.GetCollection<Account>("accounts");
        
            if (accounts.Exists(a => a.Email == newEmail && a.Id != user.Id))
                return (false, "Email already registered");

            Account? account = accounts.FindById(user.Id);
            if (account == null)
                return (false, "Account not found");

            account.Email = newEmail!;
            account.IsVerified = false;
            account.VerificationCode = GenerateSecret(32);
            account.UpdatedAt = DateTime.UtcNow;
        
            accounts.Update(account);
            return (true, "");
        });

        if (!result.Item1) {
            await SendErrorResponse(response, result.Item2);
            return;
        }

        await SendJsonResponse(response, new { success = true });
    }

    private static async Task HandleUpdatePassword(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        JObject? json = await GetRequestJson(request);
        if (json == null) {
            await SendErrorResponse(response, "Invalid request format");
            return;
        }

        string? newPassword = json["password"]?.ToString();
        if (!IsValidPassword(newPassword)) {
            await SendErrorResponse(response, "Invalid password");
            return;
        }

        bool result = DatabaseAccess.Execute(db => {
            ILiteCollection<Account>? accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindById(user.Id);
        
            if (account == null)
                return false;

            byte[] salt = GenerateSalt();
            account.PasswordHash = HashPassword(newPassword!, salt);
            account.PasswordSalt = salt;
            account.UpdatedAt = DateTime.UtcNow;
        
            accounts.Update(account);
            return true;
        });

        await SendJsonResponse(response, new { success = result });
    }

    private static async Task HandleGetFriendRequests(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        object requests = DatabaseAccess.Execute(db => {
            ILiteCollection<FriendRequest>? friendRequests = db.GetCollection<FriendRequest>("friendRequests");
            ILiteCollection<UserProfile>? profiles = db.GetCollection<UserProfile>("profiles");
            ILiteCollection<BlockedUser>? blocks = db.GetCollection<BlockedUser>("blocks");

            IEnumerable<FriendRequest>? requests = friendRequests.Find(fr => fr.ToUserId == user.Id);

            return requests.Select(fr => {
               UserProfile? profile = profiles.FindById(fr.FromUserId);
               if (profile == null) return null;

               bool isBlocked = blocks.Exists(b => (b.UserId == user.Id && b.BlockedUserId == fr.FromUserId) ||
                                                   (b.UserId == fr.FromUserId && b.BlockedUserId == user.Id));

               ILiteCollection<Friendship>? friendships = db.GetCollection<Friendship>("friendships");
               IEnumerable<ObjectId> userFriends = friendships.Find(f => f.User1Id == user.Id || f.User2Id == user.Id)
                                                              .Select(f => f.User1Id == user.Id ? f.User2Id : f.User1Id);
               IEnumerable<ObjectId> otherFriends = friendships.Find(f => f.User1Id == fr.FromUserId || f.User2Id == fr.FromUserId)
                                                               .Select(f => f.User1Id == fr.FromUserId ? f.User2Id : f.User1Id);
               List<ObjectId> mutualFriends = userFriends.Intersect(otherFriends).ToList();

               return new {
                   userId = profile.Id,
                   handle = profile.Handle,
                   displayName = profile.DisplayName,
                   bio = profile.Bio,
                   status = profile.Status,
                   mutualFriends = mutualFriends.Select(id => profiles.FindById(id)?.Handle ?? "").ToList(),
                   blocked = isBlocked
               };
            }).Where(r => r != null).ToList();
        });

        await SendJsonResponse(response, requests);
    }

    private static async Task HandleGetFriends(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        object friends = DatabaseAccess.Execute(db => {
            ILiteCollection<Friendship>? friendships = db.GetCollection<Friendship>("friendships");
            ILiteCollection<UserProfile>? profiles = db.GetCollection<UserProfile>("profiles");
            ILiteCollection<BlockedUser>? blocks = db.GetCollection<BlockedUser>("blocks");

            IEnumerable<ObjectId> friendIds = friendships.Find(f => f.User1Id == user.Id || f.User2Id == user.Id)
                                                         .Select(f => f.User1Id == user.Id ? f.User2Id : f.User1Id);

            return friendIds.Select(friendId => {
                UserProfile? profile = profiles.FindById(friendId);
                if (profile == null) return null;

                bool isBlocked = blocks.Exists(b => (b.UserId == user.Id && b.BlockedUserId == friendId) ||
                                                    (b.UserId == friendId && b.BlockedUserId == user.Id));

                IEnumerable<ObjectId> otherFriends = friendships.Find(f => f.User1Id == friendId || f.User2Id == friendId)
                                                                .Select(f => f.User1Id == friendId ? f.User2Id : f.User1Id);
                IEnumerable<ObjectId> userFriends = friendships.Find(f => f.User1Id == user.Id || f.User2Id == user.Id)
                                                               .Select(f => f.User1Id == user.Id ? f.User2Id : f.User1Id);
                List<ObjectId> mutualFriends = userFriends.Intersect(otherFriends).ToList();

                return new {
                    userId = profile.Id,
                    handle = profile.Handle,
                    displayName = profile.DisplayName,
                    bio = profile.Bio,
                    status = profile.Status,
                    mutualFriends = mutualFriends.Select(id => profiles.FindById(id)?.Handle ?? "").ToList(),
                    blocked = isBlocked
                };
            }).Where(f => f != null).ToList();
        });

        await SendJsonResponse(response, friends);
    }

    private static async Task HandleGetNotifications(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        object notifications = DatabaseAccess.Execute(db => {
            ILiteCollection<Notification>? notificationCollection = db.GetCollection<Notification>("notifications");
            return notificationCollection.Find(n => n.UserId == user.Id).OrderByDescending(n => n.CreatedAt).Select(n => new {
                senderType = n.SenderType,
                senderId = n.SenderId,
                title = n.Title,
                description = n.Description,
                attachmentUrl = n.AttachmentIds
            }).ToList();
        });

        await SendJsonResponse(response, notifications);
    }

    private static async Task HandleDeleteAccount(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        JObject? json = await GetRequestJson(request);
        if (json == null) {
            await SendErrorResponse(response, "Invalid request format");
            return;
        }

        string? username = json["username"]?.ToString();
        string? password = json["password"]?.ToString();

        if (VerifyFields(response, username, password)) return;

        bool result = DatabaseAccess.Execute(db => {
            ILiteCollection<Account>? accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindById(user.Id);
            
            if (account == null || 
                account.Handle != username || 
                !VerifyPassword(password!, account.PasswordHash, account.PasswordSalt))
                return false;

            // Delete all related data
            ILiteCollection<UserProfile>? profiles = db.GetCollection<UserProfile>("profiles");
            ILiteCollection<Session>? sessions = db.GetCollection<Session>("sessions");
            ILiteCollection<FriendRequest>? friendRequests = db.GetCollection<FriendRequest>("friendRequests");
            ILiteCollection<Friendship>? friendships = db.GetCollection<Friendship>("friendships");
            ILiteCollection<BlockedUser>? blocks = db.GetCollection<BlockedUser>("blocks");
            ILiteCollection<Notification>? notifications = db.GetCollection<Notification>("notifications");

            accounts.Delete(account.Id);
            profiles.Delete(account.Id);
            sessions.DeleteMany(s => s.UserId == account.Id);
            friendRequests.DeleteMany(fr => fr.FromUserId == account.Id || fr.ToUserId == account.Id);
            friendships.DeleteMany(f => f.User1Id == account.Id || f.User2Id == account.Id);
            blocks.DeleteMany(b => b.UserId == account.Id || b.BlockedUserId == account.Id);
            notifications.DeleteMany(n => n.UserId == account.Id);

            return true;
        });

        await SendJsonResponse(response, new { success = result });
    }
    
    #endregion Account

    #region Auth
    
    private static async Task HandleLogin(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryGetRequestJson(request, response, out JObject json)) return;

        string? username = json["username"]?.ToString();
        string? password = json["password"]?.ToString();
        string? deviceInfo = json["deviceInfo"]?.ToString();

        if (VerifyFields(response, username, password, deviceInfo)) return;

        (bool success, string error, string? authToken, string? sessionId) = DatabaseAccess.Execute(db => {
            ILiteCollection<Account> accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindOne(a => a.Handle == username);
        
            if (account == null || !VerifyPassword(password!, account.PasswordHash, account.PasswordSalt)) {
                return (false, "Invalid credentials", null, null);
            }

            //if (!account.IsVerified) {
            //    return (false, "Account not verified", null, null);
            //}

            string token = GenerateAuthToken(account);
        
            ILiteCollection<Session> sessions = db.GetCollection<Session>("sessions");
            Session session = new Session {
                UserId = account.Id,
                DeviceInfo = deviceInfo!,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };
            sessions.Insert(session);

            return (true, "", token, session.Id.ToString());
        });

        if (!success) {
            await SendErrorResponse(response, error);
            return;
        }

        await SendJsonResponse(response, new { success = true, authToken, sessionId });
    }

    private static async Task HandleGetSessions(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        List<object> sessions = DatabaseAccess.Execute(db => {
            ILiteCollection<Session> sessionsCollection = db.GetCollection<Session>("sessions");
            return sessionsCollection.Find(s => s.UserId == user.Id).Select(s => new {
                sessionId = s.Id.ToString(),
                createdAt = s.CreatedAt,
                lastUsedAt = s.LastUsedAt,
                deviceInfo = s.DeviceInfo
            }).ToList<object>();
        });

        await SendJsonResponse(response, sessions);
    }

    private static async Task HandleDeleteAllSessions(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        DatabaseAccess.Execute(db => {
            ILiteCollection<Session> sessions = db.GetCollection<Session>("sessions");
            sessions.DeleteMany(s => s.UserId == user.Id);
        });

        await SendJsonResponse(response, new { success = true });
    }

    private static async Task HandleDeleteSession(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string sessionId = request.Url!.Segments.Last();
        ObjectId id;
        try {
            id = new ObjectId(sessionId);
        } catch {
            await SendErrorResponse(response, "Invalid session ID");
            return;
        }

        bool success = DatabaseAccess.Execute(db => {
            Session? session = db.GetCollection<Session>("sessions").FindById(id);
            if (session == null || session.UserId != user.Id) {
                return false;
            }

            return db.GetCollection<Session>("sessions").Delete(id);
        });

        await SendJsonResponse(response, new { success });
    }

    private static async Task HandleGetGameToken(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string gameToken = DatabaseAccess.Execute(db => {
            ILiteCollection<Account> accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindById(user.Id);
            return account?.GameToken ?? "";
        });

        await SendJsonResponse(response, new { token = gameToken });
    }

    private static async Task HandleRegenerateGameToken(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string newToken = DatabaseAccess.Execute(db => {
            ILiteCollection<Account> accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindById(user.Id);
        
            if (account == null) {
                return "";
            }

            account.GameToken = GenerateSecret(32);
            accounts.Update(account);
            return account.GameToken;
        });

        if (string.IsNullOrEmpty(newToken)) {
            await SendErrorResponse(response, "Failed to regenerate token");
            return;
        }

        await SendJsonResponse(response, new { token = newToken });
    }

    private static async Task HandleGetJoinToken(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        (string token, DateTime expiresAt) = DatabaseAccess.Execute(db => {
            ILiteCollection<JoinToken> tokens = db.GetCollection<JoinToken>("joinTokens");
        
            tokens.DeleteMany(t => t.UserId == user.Id);
        
            JoinToken newToken = new JoinToken {
                UserId = user.Id,
                Token = GenerateSecret(32),
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
        
            tokens.Insert(newToken);
            return (newToken.Token, newToken.ExpiresAt);
        });

        await SendJsonResponse(response, new { token, expiresAt });
    }

    private static async Task HandleVerifyJoinToken(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryGetRequestJson(request, response, out JObject json)) return;

        string? username = json["username"]?.ToString();
        string? token = json["token"]?.ToString();

        if (VerifyFields(response, username, token)) return;

        bool isValid = DatabaseAccess.Execute(db => {
            ILiteCollection<Account> accounts = db.GetCollection<Account>("accounts");
            Account? account = accounts.FindOne(a => a.Handle == username);
            if (account == null) return false;

            ILiteCollection<JoinToken> tokens = db.GetCollection<JoinToken>("joinTokens");
            return tokens.Exists(t => t.UserId == account.Id && t.Token == token && t.ExpiresAt > DateTime.UtcNow);
        });

        await SendJsonResponse(response, new { valid = isValid });
    }
    
    #endregion Auth

    #region User
    
    private static async Task HandleGetUser(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        ObjectId targetId;
        try {
            targetId = new ObjectId(request.Url!.Segments.Last());
        } catch {
            await SendErrorResponse(response, "Invalid session ID");
            return;
        }

        object? userData = DatabaseAccess.Execute(db => {
            UserProfile? profile = db.GetCollection<UserProfile>("profiles").FindById(targetId);
            if (profile == null) return null;

            bool isBlocked = db.GetCollection<BlockedUser>("blocks").Exists(b => 
                (b.UserId == user.Id && b.BlockedUserId == targetId) ||
                (b.UserId == targetId && b.BlockedUserId == user.Id));

            ILiteCollection<Friendship> friendships = db.GetCollection<Friendship>("friendships");
            List<ObjectId> userFriends = friendships
                .Find(f => f.User1Id == user.Id || f.User2Id == user.Id)
                .Select(f => f.User1Id == user.Id ? f.User2Id : f.User1Id)
                .ToList();

            List<ObjectId> targetFriends = friendships
                .Find(f => f.User1Id == targetId || f.User2Id == targetId)
                .Select(f => f.User1Id == targetId ? f.User2Id : f.User1Id)
                .ToList();

            List<string> mutualFriends = userFriends.Intersect(targetFriends)
                .Select(id => db.GetCollection<UserProfile>("profiles").FindById(id)?.Handle ?? "")
                .Where(handle => !string.IsNullOrEmpty(handle))
                .ToList();

            return new {
                handle = profile.Handle,
                displayName = profile.DisplayName,
                bio = profile.Bio,
                status = profile.Status,
                createdAt = profile.CreatedAt,
                mutualFriends,
                blocked = isBlocked
            };
        });

        if (userData == null) {
            response.StatusCode = 404;
            return;
        }

        await SendJsonResponse(response, userData);
    }

    private static async Task HandleGetUsers(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;
        if (TryGetRequestJson(request, response, out JObject json)) return;

        List<ObjectId> userIds = json["userIds"]!.Values<string>().Select(id => new ObjectId(id)).ToList();

        List<object> users = DatabaseAccess.Execute(db => {
            ILiteCollection<UserProfile> profiles = db.GetCollection<UserProfile>("profiles");
            ILiteCollection<BlockedUser> blocks = db.GetCollection<BlockedUser>("blocks");
            ILiteCollection<Friendship> friendships = db.GetCollection<Friendship>("friendships");

            List<ObjectId> userFriends = friendships
                .Find(f => f.User1Id == user.Id || f.User2Id == user.Id)
                .Select(f => f.User1Id == user.Id ? f.User2Id : f.User1Id)
                .ToList();

            return userIds.Select(targetId => {
                UserProfile? profile = profiles.FindById(targetId);
                if (profile == null) return null;

                bool isBlocked = blocks.Exists(b => (b.UserId == user.Id && b.BlockedUserId == targetId) ||
                                                    (b.UserId == targetId && b.BlockedUserId == user.Id));

                List<ObjectId> targetFriends = friendships.Find(f => f.User1Id == targetId || f.User2Id == targetId)
                                                          .Select(f => f.User1Id == targetId ? f.User2Id : f.User1Id)
                                                          .ToList();

                List<string> mutualFriends = userFriends.Intersect(targetFriends)
                                                        .Select(id => profiles.FindById(id)?.Handle ?? "")
                                                        .Where(handle => !string.IsNullOrEmpty(handle))
                                                        .ToList();

                return new {
                    handle = profile.Handle,
                    displayName = profile.DisplayName,
                    bio = profile.Bio,
                    status = profile.Status,
                    createdAt = profile.CreatedAt,
                    mutualFriends,
                    blocked = isBlocked
                };
            }).Where(profile => profile != null)!.ToList<object>();
        });

        await SendJsonResponse(response, users);
    }

    private static async Task HandleAddFriend(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        ObjectId targetUserId;
        try {
            targetUserId = new ObjectId(request.Url!.Segments[^2].TrimEnd('/'));
        } catch {
            await SendErrorResponse(response, "Invalid session ID");
            return;
        }

        if (targetUserId == user.Id) {
            await SendErrorResponse(response, "Cannot friend yourself");
            return;
        }

        (bool accepted, string error) = DatabaseAccess.Execute(db => {
            if (!db.GetCollection<Account>("accounts").Exists(a => a.Id == targetUserId)) {
                return (false, "User not found");
            }

            ILiteCollection<BlockedUser> blocks = db.GetCollection<BlockedUser>("blocks");
            if (blocks.Exists(b => 
                (b.UserId == user.Id && b.BlockedUserId == targetUserId) ||
                (b.UserId == targetUserId && b.BlockedUserId == user.Id))) {
                return (false, "Cannot friend blocked user");
            }

            ILiteCollection<Friendship> friendships = db.GetCollection<Friendship>("friendships");
            if (friendships.Exists(f => 
                (f.User1Id == user.Id && f.User2Id == targetUserId) ||
                (f.User1Id == targetUserId && f.User2Id == user.Id))) {
                return (false, "Already friends");
            }

            ILiteCollection<FriendRequest> requests = db.GetCollection<FriendRequest>("friendRequests");

            if (requests.Exists(r => r.FromUserId == user.Id && r.ToUserId == targetUserId)) {
                return (false, "Friend request already sent");
            }

            FriendRequest? theirRequest = requests.FindOne(r => r.FromUserId == targetUserId && r.ToUserId == user.Id);

            if (theirRequest != null) {
                requests.Delete(theirRequest.Id);
                
                Friendship friendship = new Friendship {
                    User1Id = user.Id,
                    User2Id = targetUserId,
                    CreatedAt = DateTime.UtcNow
                };
                friendships.Insert(friendship);

                Notification acceptNotification = new Notification {
                    UserId = targetUserId,
                    SenderType = "user",
                    SenderId = user.Id.ToString(),
                    Title = $"{user.Handle} accepted your friend request",
                    Description = "",
                    CreatedAt = DateTime.UtcNow
                };
                db.GetCollection<Notification>("notifications").Insert(acceptNotification);

                return (true, "");
            }

            FriendRequest friendRequest = new FriendRequest {
                FromUserId = user.Id,
                ToUserId = targetUserId,
                CreatedAt = DateTime.UtcNow
            };
            requests.Insert(friendRequest);

            Notification notification = new Notification {
                UserId = targetUserId,
                SenderType = "user",
                SenderId = user.Id.ToString(),
                Title = $"{user.Handle} sent you a friend request",
                Description = "",
                CreatedAt = DateTime.UtcNow
            };
            db.GetCollection<Notification>("notifications").Insert(notification);

            return (false, "");
        });

        await SendJsonResponse(response, new { accepted, error });
    }

    private static async Task HandleRemoveFriend(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        ObjectId targetUserId;
        try {
            targetUserId = new ObjectId(request.Url!.Segments[^2].TrimEnd('/'));
        } catch {
            await SendErrorResponse(response, "Invalid session ID");
            return;
        }

        bool success = DatabaseAccess.Execute(db => {
            ILiteCollection<Friendship> friendships = db.GetCollection<Friendship>("friendships");
            int deleted = friendships.DeleteMany(f => (f.User1Id == user.Id && f.User2Id == targetUserId) ||
                                                      (f.User1Id == targetUserId && f.User2Id == user.Id));

            ILiteCollection<FriendRequest> requests = db.GetCollection<FriendRequest>("friendRequests");
            requests.DeleteMany(r => (r.FromUserId == user.Id && r.ToUserId == targetUserId) ||
                                     (r.FromUserId == targetUserId && r.ToUserId == user.Id));

            return deleted > 0;
        });

        await SendJsonResponse(response, new { success });
    }

    private static async Task HandleBlockUser(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        ObjectId targetUserId;
        try {
            targetUserId = new ObjectId(request.Url!.Segments[^2].TrimEnd('/'));
        } catch {
            await SendErrorResponse(response, "Invalid session ID");
            return;
        }

        if (targetUserId == user.Id) {
            await SendErrorResponse(response, "Cannot block yourself");
            return;
        }

        bool success = DatabaseAccess.Execute(db => {
            if (!db.GetCollection<Account>("accounts").Exists(a => a.Id == targetUserId)) {
                return false;
            }

            ILiteCollection<BlockedUser> blocks = db.GetCollection<BlockedUser>("blocks");
            if (blocks.Exists(b => b.UserId == user.Id && b.BlockedUserId == targetUserId)) {
                blocks.DeleteMany(b => b.UserId == user.Id && b.BlockedUserId == targetUserId);
            } else {
                BlockedUser block = new BlockedUser {
                    UserId = user.Id,
                    BlockedUserId = targetUserId,
                    CreatedAt = DateTime.UtcNow
                };
                blocks.Insert(block);

                ILiteCollection<Friendship> friendships = db.GetCollection<Friendship>("friendships");
                friendships.DeleteMany(f => (f.User1Id == user.Id && f.User2Id == targetUserId) ||
                                            (f.User1Id == targetUserId && f.User2Id == user.Id));

                ILiteCollection<FriendRequest> requests = db.GetCollection<FriendRequest>("friendRequests");
                requests.DeleteMany(r => (r.FromUserId == user.Id && r.ToUserId == targetUserId) ||
                                         (r.FromUserId == targetUserId && r.ToUserId == user.Id));
            }

            return true;
        });

        await SendJsonResponse(response, new { success });
    }
    
    #endregion User

    #region Misc
    
    private static async Task HandleSearch(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        Dictionary<string, string> queryParams = GetQueryParameters(request);
        string? type = queryParams.GetValueOrDefault("type");
        string? name = queryParams.GetValueOrDefault("name");
        string? sortBy = queryParams.GetValueOrDefault("sort");
        string? filterTags = queryParams.GetValueOrDefault("filter");
        int page = int.Parse(queryParams.GetValueOrDefault("page", "0"));
        int limit = int.Parse(queryParams.GetValueOrDefault("limit", "20"));

        if (string.IsNullOrEmpty(type)) {
            await SendErrorResponse(response, "Search type required");
            return;
        }

        object result = type switch {
            "user" => SearchUsers(user.Id, name, page, limit),
            "avatar" => SearchContent(user.Id, "avatar", name, filterTags, sortBy, page, limit),
            "world" => SearchContent(user.Id, "world", name, filterTags, sortBy, page, limit),
            "prop" => SearchContent(user.Id, "prop", name, filterTags, sortBy, page, limit),
            _ => new { items = new List<object>(), hasMore = false }
        };

        await SendJsonResponse(response, result);
    }
    
    private static async Task HandleGetImage(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account _)) return;

        string fileId = request.Url!.Segments.Last();
    
        byte[]? imageData = DatabaseAccess.Execute(db => {
            LiteFileInfo<string>? fileInfo = db.FileStorage.FindById(fileId);
            if (fileInfo == null) {
                return null;
            }

            using MemoryStream ms = new MemoryStream();
            fileInfo.CopyTo(ms);
            return ms.ToArray();
        });

        if (imageData == null) {
            response.StatusCode = 404;
            return;
        }

        response.ContentType = Path.GetExtension(fileId).ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        await response.OutputStream.WriteAsync(imageData);
    }
    
    #endregion Misc

    #region Content

    private static async Task HandleCreateContent(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        JObject? json = await GetRequestJson(request);
        string? contentType = json?["contentType"]?.ToString();
        if (string.IsNullOrEmpty(contentType)) {
            await SendErrorResponse(response, "Content type required");
            return;
        }

        if (contentType != "avatar" && contentType != "prop" && contentType != "world" && contentType != "gamemode") {
            await SendErrorResponse(response, "Invalid content type");
            return;
        }

        Guid contentId = DatabaseAccess.Execute(db => {
            Guid contentId = Guid.NewGuid();
            Content content = new Content {
                Id = contentId,
                OwnerId = user.Id,
                ContentType = contentType,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsPublic = false
            };

            db.GetCollection<Content>("content").Insert(content);
            return content.Id;
        });

        await SendJsonResponse(response, new { contentId });
    }

    private static async Task HandleUpdateContent(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string contentId = request.Url!.Segments.Last();
        if (!Guid.TryParse(contentId, out Guid id)) {
            await SendErrorResponse(response, "Invalid content ID");
            return;
        }

        MultipartFormDataParser? formData = await GetMultipartFormData(request);
        if (formData == null) {
            await SendErrorResponse(response, "Invalid request format");
            return;
        }

        (bool success, string error) = DatabaseAccess.Execute(db => {
            ILiteCollection<Content> contents = db.GetCollection<Content>("content");
            Content? content = contents.FindById(id);
            
            if (content == null || content.OwnerId != user.Id) {
                return (false, "Content not found or unauthorized");
            }
            
            ILiteStorage<string> fs = db.FileStorage;
            FilePart? contentFile = formData.Files.FirstOrDefault(f => f.Name == "file");
            if (contentFile != null) {
                fs.Delete(content.Id.ToString());
                fs.Upload(content.Id.ToString(), contentFile.FileName, contentFile.Data);
            }
            
            FilePart? thumbnailFile = formData.Files.FirstOrDefault(f => f.Name == "thumbnail");
            if (thumbnailFile != null) {
                fs.Delete($"{content.Id}-thumbnail");
                fs.Upload($"{content.Id}-thumbnail", thumbnailFile.FileName, thumbnailFile.Data);
            }

            string? metadataJson = formData.GetParameterValue("metadata");
            if (!string.IsNullOrEmpty(metadataJson)) {
                JObject updates = JObject.Parse(metadataJson);

                if (updates["name"] != null)
                    content.Name = updates["name"]!.ToString();
                if (updates["description"] != null)
                    content.Description = updates["description"]!.ToString();
                if (updates["contentWarningTags"] != null)
                    content.ContentWarningTags = updates["contentWarningTags"]!.ToObject<List<string>>() ?? [];
            }

            content.UpdatedAt = DateTime.UtcNow;
            contents.Update(content);
            
            return (true, "");
        });

        if (!success) {
            await SendErrorResponse(response, error);
            return;
        }

        await SendJsonResponse(response, new { success = true });
    }

    private static async Task HandleShareContent(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string contentId = request.Url!.Segments[^2].TrimEnd('/');
        if (!Guid.TryParse(contentId, out Guid id)) {
            await SendErrorResponse(response, "Invalid content ID");
            return;
        }

        JObject? json = await GetRequestJson(request);
        if (json == null || json["userIds"]?.Type != JTokenType.Array) {
            await SendErrorResponse(response, "Invalid request format");
            return;
        }

        List<ObjectId> userIds = json["userIds"]!.Values<string>().Select(userIds => new ObjectId(userIds)).ToList();

        (bool success, string error) = DatabaseAccess.Execute(db => {
            ILiteCollection<Content> contents = db.GetCollection<Content>("content");
            Content? content = contents.FindById(id);
        
            if (content == null || content.OwnerId != user.Id) {
                return (false, "Content not found or unauthorized");
            }

            content.SharedWithUserIds = userIds;
            content.UpdatedAt = DateTime.UtcNow;
            contents.Update(content);
        
            return (true, "");
        });

        if (!success) {
            await SendErrorResponse(response, error);
            return;
        }

        await SendJsonResponse(response, new { success = true });
    }

    private static async Task HandleGetAllContent(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        List<object> contents = DatabaseAccess.Execute(db => {
            return db.GetCollection<Content>("content").Find(c => c.OwnerId == user.Id).Select(c => new {
                id = c.Id,
                name = c.Name,
                description = c.Description,
                ownerId = c.OwnerId,
                isPublic = c.IsPublic,
                createdAt = c.CreatedAt,
                updatedAt = c.UpdatedAt,
                contentWarningTags = c.ContentWarningTags,
                contentType = c.ContentType,
                sharedWithUserIds = c.SharedWithUserIds
            }).ToList<object>();
        });

        await SendJsonResponse(response, contents);
    }

    private static async Task HandleGetContent(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string contentId = request.Url!.Segments.Last();
        if (!Guid.TryParse(contentId, out Guid id)) {
            await SendErrorResponse(response, "Invalid content ID");
            return;
        }

        object? contentData = DatabaseAccess.Execute(db => {
            Content? content = db.GetCollection<Content>("content").FindById(id);
            if (content == null) return null;

            if (!content.IsPublic && content.OwnerId != user.Id && !content.SharedWithUserIds.Contains(user.Id)) {
                return null;
            }

            return new {
                id = content.Id,
                name = content.Name,
                description = content.Description,
                ownerId = content.OwnerId,
                isPublic = content.IsPublic,
                createdAt = content.CreatedAt,
                updatedAt = content.UpdatedAt,
                contentWarningTags = content.ContentWarningTags,
                contentType = content.ContentType,
                sharedWithUserIds = content.OwnerId == user.Id ? content.SharedWithUserIds : null
            };
        });

        if (contentData == null) {
            response.StatusCode = 404;
            return;
        }

        await SendJsonResponse(response, contentData);
    }

    private static async Task HandleDownloadContent(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string contentId = request.Url!.Segments[^2].TrimEnd('/');
        if (!Guid.TryParse(contentId, out Guid id)) {
            await SendErrorResponse(response, "Invalid content ID");
            return;
        }

        (bool success, string? fileName, Stream? stream) = DatabaseAccess.Execute(db => {
            Content? content = db.GetCollection<Content>("content").FindById(id);
            if (content == null) return (false, null, null);

            if (!content.IsPublic && content.OwnerId != user.Id && !content.SharedWithUserIds.Contains(user.Id)) {
                return (false, null, null);
            }

            ILiteStorage<string> storage = db.FileStorage;
            LiteFileInfo<string>? file = storage.FindById(id.ToString());
            return file == null ? (false, null, null) : (true, file.Filename, file.OpenRead());
        });

        if (!success || fileName == null || stream == null) {
            response.StatusCode = 404;
            return;
        }

        try {
            response.ContentType = "application/octet-stream";
            response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            await using (stream) {
                await stream.CopyToAsync(response.OutputStream);
            }
        }
        catch {
            response.StatusCode = 500;
        }
    }

    private static async Task HandleDeleteContent(HttpListenerRequest request, HttpListenerResponse response) {
        if (TryAuthenticateRequest(request, response, out Account user)) return;

        string contentId = request.Url!.Segments.Last();
        if (!Guid.TryParse(contentId, out Guid id)) {
            await SendErrorResponse(response, "Invalid content ID");
            return;
        }

        bool success = DatabaseAccess.Execute(db => {
            Content? content = db.GetCollection<Content>("content").FindById(id);
            if (content == null || content.OwnerId != user.Id) {
                return false;
            }

            db.GetCollection<Content>("content").Delete(id);
            db.FileStorage.Delete($"{content.ContentType}_{id}");
            db.FileStorage.Delete($"{content.ContentType}_{id}-thumbnail");
            return true;
        });

        await SendJsonResponse(response, new { success });
    }

    #endregion Content
}