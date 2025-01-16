namespace VRroomAPI;
public static class Program {
	private static void Main(string[] args) {
		HttpApi.Start();
		while (true) {
			Thread.Sleep(1000);
		}
	}
}