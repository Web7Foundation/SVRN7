using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Web7.SVRN7.Apps
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.SetHighDpiMode(HighDpiMode.SystemAware);
			Application.Run(new MainForm());
		}
	}
}