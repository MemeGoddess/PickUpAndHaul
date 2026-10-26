namespace PickUpAndHaul
{
	static class Log
	{
		[System.Diagnostics.Conditional("DEBUG")]
		public static void Message(string x) => Verse.Log.Message(x);

		[System.Diagnostics.Conditional("DEBUG_EXTRA_LOGS")]
		public static void Info(string x) => Verse.Log.Message(x);
	}
}
