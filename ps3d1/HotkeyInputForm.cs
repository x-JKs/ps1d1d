using System;
using System.Drawing;
using System.Windows.Forms;

namespace ps3d1
{
    /// <summary>
    /// Form for capturing hotkey input
    /// </summary>
    public class HotkeyInputForm : Form
    {
        public Keys SelectedHotkey { get; private set; }
        private Label lblInstruction;
        private Label lblCurrentKey;
        private Button btnOK;
        private Button btnClear;
        private Button btnCancel;

        public HotkeyInputForm(Keys currentHotkey = Keys.None)
        {
            SelectedHotkey = currentHotkey;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(300, 180);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Set Hotkey";
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.KeyPreview = true;

            lblInstruction = new Label
            {
                Text = "Press any key to set as hotkey:",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.White,
                Location = new Point(20, 20),
                AutoSize = true
            };
            this.Controls.Add(lblInstruction);

            lblCurrentKey = new Label
            {
                Text = SelectedHotkey == Keys.None ? "(No hotkey set)" : SelectedHotkey.ToString(),
                Font = new Font("Segoe UI Semibold", 12f),
                ForeColor = Color.FromArgb(100, 160, 255),
                Location = new Point(20, 50),
                Size = new Size(260, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(40, 40, 40)
            };
            this.Controls.Add(lblCurrentKey);

            btnOK = new Button
            {
                Text = "OK",
                Location = new Point(20, 100),
                Size = new Size(75, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(100, 160, 255),
                ForeColor = Color.White,
                DialogResult = DialogResult.OK
            };
            btnOK.FlatAppearance.BorderSize = 0;
            this.Controls.Add(btnOK);

            btnClear = new Button
            {
                Text = "Clear",
                Location = new Point(105, 100),
                Size = new Size(75, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White
            };
            btnClear.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnClear.Click += (s, e) =>
            {
                SelectedHotkey = Keys.None;
                lblCurrentKey.Text = "(No hotkey set)";
            };
            this.Controls.Add(btnClear);

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(190, 100),
                Size = new Size(75, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Don't capture Enter or Escape
            if (keyData == Keys.Enter || keyData == Keys.Escape)
                return base.ProcessCmdKey(ref msg, keyData);

            // Capture the key
            SelectedHotkey = keyData;
            lblCurrentKey.Text = keyData.ToString();
            return true;
        }
    }
}
