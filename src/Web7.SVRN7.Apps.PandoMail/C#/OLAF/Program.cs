using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Web7.SVRN7.Apps
{
	static class Program
	{
		internal static int TdaPort = 8443;

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			for (int i = 0; i < args.Length - 1; i++)
				if (args[i] == "--port" && int.TryParse(args[i + 1], out int p))
					TdaPort = p;

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.SetHighDpiMode(HighDpiMode.SystemAware);
			Application.Run(new MainForm());
		}
	}
}
