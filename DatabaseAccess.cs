using JetBrains.Annotations;
using LiteDB;

namespace VRroomAPI.Database;
[PublicAPI]
public static class DatabaseAccess {
	private static LiteDatabase _db = new("data.db");
	private static object _lock = new();

	public static T Execute<T>(Func<LiteDatabase, T> action) {
		lock (_lock) {
			return action(_db);
		}
	}
	
	public static void Execute(Action<LiteDatabase> action) {
		lock (_lock) {
			action(_db);
		}
	}
}