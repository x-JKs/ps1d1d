using System;
using System.Windows.Forms;
using ps3d1.Security;

namespace ps3d1
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

            // Initialize authentication system
            Authentication.Initialize();

            // Check if already authenticated (saved credentials)
            if (Authentication.IsAuthenticated())
            {
                // Already authenticated, go directly to main form
                Application.Run(new Form1());
            }
            else
            {
                // Show login form first
                using (var loginForm = new LoginForm())
                {
                    if (loginForm.ShowDialog() == DialogResult.OK)
                    {
                        // Login successful, show main form
                        Application.Run(new Form1());
                    }
                }
            }

            // Shutdown authentication system
            Authentication.Shutdown();
        }
    }
}
