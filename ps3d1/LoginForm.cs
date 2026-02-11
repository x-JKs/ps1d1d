using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using ps3d1.Security;

namespace ps3d1
{
    /// <summary>
    /// Login form styled like Loader_V1 ImGui authentication screen
    /// </summary>
    public class LoginForm : Form
    {
        private TextBox txtUsername;
        private TextBox txtLicenseKey;
        private CheckBox chkRememberMe;
        private Button btnLogin;
        private Label lblError;
        private Label lblStatus;
        private ProgressBar progressAuth;
        private bool isDragging = false;
        private Point dragOffset;

        public LoginForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            InitializeComponent();
            LoadSavedCredentials();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        private void InitializeComponent()
        {
            this.Size = new Size(380, 320);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.Text = "Opium";

            // Enable dragging
            this.MouseDown += Form_MouseDown;
            this.MouseMove += Form_MouseMove;
            this.MouseUp += Form_MouseUp;
            this.Paint += LoginForm_Paint;

            // Close button
            var btnClose = new Label
            {
                Text = "×",
                Font = new Font("Segoe UI", 14f),
                ForeColor = Color.FromArgb(105, 105, 105),
                Size = new Size(30, 30),
                Location = new Point(this.Width - 35, 5),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (s, e) => Application.Exit();
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.FromArgb(220, 80, 80);
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Color.FromArgb(105, 105, 105);
            this.Controls.Add(btnClose);

            // Title
            var lblTitle = new Label
            {
                Text = "Opium",
                Font = new Font("Segoe UI Semibold", 12f),
                ForeColor = Color.White,
                Location = new Point(20, 25),
                AutoSize = true
            };
            this.Controls.Add(lblTitle);

            // Separator
            var separator = new Panel
            {
                Location = new Point(20, 55),
                Size = new Size(this.Width - 40, 1),
                BackColor = Color.FromArgb(50, 50, 50)
            };
            this.Controls.Add(separator);

            int y = 75;

            // Username
            var lblUsername = new Label
            {
                Text = "Username:",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.White,
                Location = new Point(20, y),
                AutoSize = true
            };
            this.Controls.Add(lblUsername);
            y += 22;

            txtUsername = new TextBox
            {
                Location = new Point(20, y),
                Size = new Size(this.Width - 40, 26),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };
            this.Controls.Add(txtUsername);
            y += 40;

            // License Key
            var lblKey = new Label
            {
                Text = "License Key:",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.White,
                Location = new Point(20, y),
                AutoSize = true
            };
            this.Controls.Add(lblKey);
            y += 22;

            txtLicenseKey = new TextBox
            {
                Location = new Point(20, y),
                Size = new Size(this.Width - 40, 26),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f),
                UseSystemPasswordChar = true
            };
            this.Controls.Add(txtLicenseKey);
            y += 35;

            // Format hint
            var lblHint = new Label
            {
                Text = "License key format: XXXX-XXXX-XXXX-XXXX",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(105, 105, 105),
                Location = new Point(20, y),
                AutoSize = true
            };
            this.Controls.Add(lblHint);
            y += 25;

            // Remember Me
            chkRememberMe = new CheckBox
            {
                Text = "Remember Me",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.White,
                Location = new Point(17, y),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat
            };
            this.Controls.Add(chkRememberMe);
            y += 35;

            // Login button
            btnLogin = new Button
            {
                Text = "Login",
                Location = new Point(20, y),
                Size = new Size(this.Width - 40, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(100, 160, 255),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10f),
                Cursor = Cursors.Hand
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.Click += BtnLogin_Click;
            this.Controls.Add(btnLogin);
            y += 50;

            // Error message
            lblError = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(220, 80, 80),
                Location = new Point(20, y),
                Size = new Size(this.Width - 40, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblError);
            y += 25;

            // Status
            lblStatus = new Label
            {
                Text = "Hooks will initialize after login",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(105, 105, 105),
                Location = new Point(20, y),
                AutoSize = true
            };
            this.Controls.Add(lblStatus);
            y += 22;

            progressAuth = new ProgressBar
            {
                Location = new Point(20, y),
                Size = new Size(this.Width - 40, 6),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 24,
                Visible = false
            };
            this.Controls.Add(progressAuth);

            // Enter key to submit
            this.AcceptButton = btnLogin;
        }

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragOffset = e.Location;
            }
        }

        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                var p = this.PointToScreen(e.Location);
                this.Location = new Point(p.X - dragOffset.X, p.Y - dragOffset.Y);
            }
        }

        private void Form_MouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
        }

        private void LoginForm_Paint(object sender, PaintEventArgs e)
        {
            // Draw border
            using (var pen = new Pen(Color.FromArgb(50, 50, 50), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        private void LoadSavedCredentials()
        {
            string username, licenseKey;
            if (Authentication.GetSavedCredentials(out username, out licenseKey))
            {
                txtUsername.Text = username;
                txtLicenseKey.Text = licenseKey;
                chkRememberMe.Checked = true;
            }
        }

        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string licenseKey = txtLicenseKey.Text.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(licenseKey))
            {
                lblError.Text = "Please enter username and license key.";
                return;
            }

            lblError.Text = "";
            SetAuthInProgress(true);

            try
            {
                bool success = await Task.Run(() => Authentication.Login(username, licenseKey));
                if (success)
                {
                    // Save credentials if remember me is checked
                    Authentication.SaveCredentials(chkRememberMe.Checked);

                    lblStatus.Text = "Authentication successful!";
                    lblStatus.ForeColor = Color.FromArgb(80, 200, 120);

                    // Close login and show main form
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    lblError.Text = "Invalid license key or HWID mismatch.";
                    txtLicenseKey.Clear();
                    SetAuthInProgress(false);
                }
            }
            catch (Exception ex)
            {
                lblError.Text = "Connection error: " + ex.Message;
                SetAuthInProgress(false);
            }
        }

        private void SetAuthInProgress(bool inProgress)
        {
            txtUsername.Enabled = !inProgress;
            txtLicenseKey.Enabled = !inProgress;
            chkRememberMe.Enabled = !inProgress;
            btnLogin.Enabled = !inProgress;
            btnLogin.Text = inProgress ? "Authenticating..." : "Login";
            progressAuth.Visible = inProgress;
        }
    }
}
