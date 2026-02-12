using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using soulsAPI;
using ps3d1.Security;

namespace ps3d1
{
    public partial class Form1 : Form
    {
        private static api api = new api();
        private System.Windows.Forms.Timer enemyVoidTimer;
        private System.Windows.Forms.Timer licenseRefreshTimer;
        private System.Windows.Forms.Timer snowTimer;
        
        private List<SavedPosition> savedPositions = new List<SavedPosition>();
        private Dictionary<Keys, SavedPosition> hotkeyMap = new Dictionary<Keys, SavedPosition>();
        private readonly List<FeatureHotkey> featureHotkeys = new List<FeatureHotkey>();
        private readonly Dictionary<Keys, FeatureHotkey> featureHotkeyMap = new Dictionary<Keys, FeatureHotkey>();
        private FeatureHotkey selectedFeatureHotkey;
        
        // Teleport to Selected hotkey
        private Keys teleportToSelectedHotkey = Keys.None;
        private int selectedPositionIndex = -1;
        
        private bool isDragging = false;
        private Point dragOffset;

        // Current tab
        private int currentTab = 0;
        
        // Connection state
        private bool isConnected = false;
        private bool hookInstalled = false;

        // Application theme palette (minimal dark)
        public static class Theme
        {
            public static Color Background = Color.FromArgb(18, 18, 18);
            public static Color LeftPanel = Color.FromArgb(24, 24, 24);
            public static Color ContentBg = Color.FromArgb(30, 30, 30);
            public static Color CardBg = Color.FromArgb(41, 41, 41);
            public static Color CardBorder = Color.FromArgb(50, 50, 50);
            public static Color InputBg = Color.FromArgb(35, 35, 35);
            public static Color Text = Color.FromArgb(255, 255, 255);
            public static Color TextDim = Color.FromArgb(105, 105, 105);
            public static Color TextMuted = Color.FromArgb(70, 70, 70);
            public static Color Accent = Color.FromArgb(120, 120, 120);
            public static Color AccentHover = Color.FromArgb(150, 150, 150);
            public static Color Success = Color.FromArgb(80, 200, 120);
            public static Color Danger = Color.FromArgb(220, 80, 80);
            public static Color DangerHover = Color.FromArgb(240, 100, 100);
            public static Color TabActive = Color.FromArgb(45, 45, 45);
            public static Color TabHover = Color.FromArgb(38, 38, 38);
            public static Color Selected = Color.FromArgb(65, 65, 65);
        }

        private class ActivityOption
        {
            public string Name { get; }
            public ushort Index { get; }

            public ActivityOption(string name, ushort index)
            {
                Name = name;
                Index = index;
            }

            public override string ToString() => $"{Name} ({Index})";
        }

        public class SavedPosition
        {
            public string Name { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public Keys Hotkey { get; set; } = Keys.None;
            public override string ToString() => Hotkey != Keys.None ? $"{Name} [{Hotkey}]" : Name;
        }

        private class FeatureHotkey
        {
            public string Name { get; }
            public string Category { get; }
            public Action Action { get; }
            public Keys Hotkey { get; set; }

            public FeatureHotkey(string name, string category, Action action)
            {
                Name = name;
                Category = category;
                Action = action;
                Hotkey = Keys.None;
            }
        }

        public Form1()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetupTimers();
            SetupUI();
            this.KeyPreview = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (featureHotkeyMap.TryGetValue(keyData, out var featureHotkey))
            {
                featureHotkey.Action?.Invoke();
                return true;
            }

            if (hotkeyMap.ContainsKey(keyData))
            {
                var pos = hotkeyMap[keyData];
                TeleportToPosition(pos);
                return true;
            }
            
            if (keyData == teleportToSelectedHotkey && teleportToSelectedHotkey != Keys.None)
            {
                if (selectedPositionIndex >= 0 && selectedPositionIndex < savedPositions.Count)
                {
                    TeleportToPosition(savedPositions[selectedPositionIndex]);
                }
                return true;
            }
            
            if (keyData == Keys.Up && savedPositions.Count > 0)
            {
                selectedPositionIndex = Math.Max(0, selectedPositionIndex - 1);
                RefreshPositionList();
                return true;
            }
            if (keyData == Keys.Down && savedPositions.Count > 0)
            {
                selectedPositionIndex = Math.Min(savedPositions.Count - 1, selectedPositionIndex + 1);
                RefreshPositionList();
                return true;
            }
            
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SetupTimers()
        {
            enemyVoidTimer = new System.Windows.Forms.Timer { Interval = 50 };
            enemyVoidTimer.Tick += EnemyVoidTimer_Tick;

            licenseRefreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            licenseRefreshTimer.Tick += (s, e) => UpdateLicenseLabels();

        }

        private void SetupUI()
        {
            this.Size = new Size(838, 600);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Theme.Background;
            this.Text = string.Empty;

            this.MouseDown += Form_MouseDown;
            this.MouseMove += Form_MouseMove;
            this.MouseUp += Form_MouseUp;
            this.Paint += Form1_Paint;
            this.Shown += (s, e) => UpdateLicenseLabels();
            this.FormClosed += (s, e) =>
            {
                SaveHotkeyPreferences();
                licenseRefreshTimer?.Stop();
                Authentication.AuthStateChanged -= HandleAuthStateChanged;
            };

            Authentication.AuthStateChanged += HandleAuthStateChanged;

            BuildUI();
            licenseRefreshTimer.Start();
        }

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < 38)
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

        // ═══════════════════════════════════════════════════════════════
        // UI BUILDING
        // ═══════════════════════════════════════════════════════════════

        private TabButton[] tabButtons;
        private Panel panelContent;
        private Panel[] contentPanels;
        private Label lblUserInfo, lblLicenseInfo;
        private Panel panelLeft;
        private Label lblTitle;
        private Label btnClose;
        private Label btnMin;
        private Label lblLogo;
        private Panel teleportPanel;

        // Content controls
        private Label lblStatusValue;
        private TextBox txtConsoleIp;
        private readonly string ccapiTargetPath = Path.Combine(Application.StartupPath, "ccapi_target.txt");
        private TextBox txtTeleX, txtTeleY, txtTeleZ;
        private ListView lvPositions;
        private Label lblTeleportHotkey;
        private TextBox txtIdentityName;
        private ComboBox cmbEmblemId;
        private ListView lvFeatureHotkeys;
        private Label lblSelectedHotkeyFeature;
        private readonly string hotkeyPrefsPath = Path.Combine(Application.StartupPath, "hotkeys.cfg");
        private readonly string activityDataPath = Path.Combine(Application.StartupPath, "activities.json");
        private readonly List<ActivityOption> activityOptions = new List<ActivityOption>();
        private bool suppressActivityTextFilter;
        
        // Activity Loader controls
        private ComboBox cmbActivityIndex;
        private Button btnReadActivity;
        private Button btnWriteActivity;

        // All Toggles - From OverSRC
        private ToggleSwitch tglGodMode, tglImmune, tglNoTarget, tglNoCollision, tglSuperSpeed;
        private ToggleSwitch tglInfiniteAmmo, tglUnlimitedSparrow, tglUnlimitedAbilities;
        private ToggleSwitch tglNoRecoil, tglOneHitKill, tglRapidFire, tglNoSpread;
        private ToggleSwitch tglEnemiesVoid, tglInstantRevive, tglStopBullets, tglStaticActors, tglGameSpeed;
        private TrackBar sliderFOV, sliderGameSpeed;
        private Label lblFOVValue, lblGameSpeedValue;

        private void BuildUI()
        {
            // === TITLE BAR ===
            lblTitle = new Label
            {
                Text = "ps3d1",
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = Theme.Text,
                Location = new Point(14, 9),
                AutoSize = true,
                Visible = true
            };
            this.Controls.Add(lblTitle);

            btnClose = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 12f),
                ForeColor = Theme.TextDim,
                Location = new Point(this.Width - 35, 7),
                Size = new Size(25, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (s, e) => this.Close();
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Theme.Danger;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Theme.TextDim;
            this.Controls.Add(btnClose);

            btnMin = new Label
            {
                Text = "─",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Theme.TextDim,
                Location = new Point(this.Width - 65, 7),
                Size = new Size(25, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnMin.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
            btnMin.MouseEnter += (s, e) => btnMin.ForeColor = Theme.Text;
            btnMin.MouseLeave += (s, e) => btnMin.ForeColor = Theme.TextDim;
            this.Controls.Add(btnMin);

            // === LEFT PANEL WITH SCROLL ===
            panelLeft = new DarkScrollPanel
            {
                Location = new Point(0, 38),
                Size = new Size(161, this.Height - 38),
                BackColor = Theme.LeftPanel,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                AutoScroll = true
            };
            this.Controls.Add(panelLeft);

            lblLogo = new Label
            {
                Text = "Ω",
                Font = new Font("Segoe UI Symbol", 28f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                Location = new Point(0, 10),
                Size = new Size(panelLeft.Width - 20, 48),
                TextAlign = ContentAlignment.MiddleCenter
            };
            panelLeft.Controls.Add(lblLogo);

            // Tab buttons - Updated tabs
            string[] tabNames = { "Main", "Position", "Misc", "Hotkeys" };
            tabButtons = new TabButton[tabNames.Length];

            int tabY = 70;
            for (int i = 0; i < tabNames.Length; i++)
            {
                int idx = i;
                tabButtons[i] = new TabButton(tabNames[i], i == 0)
                {
                    Location = new Point(6, tabY)
                };
                tabButtons[i].Click += (s, e) => SelectTab(idx);
                panelLeft.Controls.Add(tabButtons[i]);
                tabY += 36;
            }


            lblUserInfo = new Label
            {
                Text = "User: Checking...",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Theme.TextDim,
                Location = new Point(10, panelLeft.Height - 55),
                AutoSize = true
            };
            panelLeft.Controls.Add(lblUserInfo);

            lblLicenseInfo = new Label
            {
                Text = "License: Checking...",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Theme.Success,
                Location = new Point(10, panelLeft.Height - 35),
                AutoSize = true
            };
            panelLeft.Controls.Add(lblLicenseInfo);

            // === CONTENT AREA ===
            panelContent = new Panel
            {
                Location = new Point(161, 38),
                Size = new Size(this.Width - 161, this.Height - 38),
                BackColor = Theme.ContentBg,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(panelContent);

            BuildContentPanels();
            InitializeFeatureHotkeys();
            InitializeActivityCombo();
            LoadHotkeyPreferences();
            LoadSavedCcapiTarget();

            UpdateLicenseLabels();
            UpdateLayoutSizing();
        }

        private void SelectTab(int index)
        {
            currentTab = index;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                tabButtons[i].IsSelected = (i == index);
                tabButtons[i].Invalidate();
            }
            for (int i = 0; i < contentPanels.Length; i++)
            {
                contentPanels[i].Visible = (i == index);
            }
        }

        private void UpdateLicenseLabels()
        {
            var info = Authentication.GetLicenseInfo();
            bool isAuthenticated = Authentication.IsAuthenticated();
            bool isExpired = Authentication.IsLicenseExpired();

            if (!isAuthenticated)
            {
                lblUserInfo.Text = "User: Guest";
                lblLicenseInfo.Text = "License: Not Authenticated";
                lblLicenseInfo.ForeColor = Theme.Danger;
                return;
            }

            lblUserInfo.Text = $"User: {info.Username}";

            string licenseType = string.IsNullOrWhiteSpace(info.LicenseType) ? "" : info.LicenseType.Trim();
            if (info.IsPermanent || string.Equals(licenseType, "lifetime", StringComparison.OrdinalIgnoreCase))
            {
                lblLicenseInfo.Text = "License: Lifetime";
                lblLicenseInfo.ForeColor = Theme.Success;
                return;
            }

            if (string.IsNullOrWhiteSpace(licenseType))
            {
                lblLicenseInfo.Text = info.DaysRemaining > 0
                    ? $"License: {info.DaysRemaining} days"
                    : "License: Active";
            }
            else
            {
                string formattedType = char.ToUpperInvariant(licenseType[0]) + licenseType.Substring(1);
                lblLicenseInfo.Text = info.DaysRemaining > 0
                    ? $"License: {formattedType} ({info.DaysRemaining} days)"
                    : $"License: {formattedType}";
            }

            if (isExpired)
            {
                lblLicenseInfo.Text = "License: Expired";
                lblLicenseInfo.ForeColor = Theme.Danger;
                return;
            }

            lblLicenseInfo.ForeColor = Theme.Success;
        }

        private void HandleAuthStateChanged()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateLicenseLabels));
                return;
            }

            UpdateLicenseLabels();
        }

        private void BuildContentPanels()
        {
            contentPanels = new Panel[4];
            for (int i = 0; i < contentPanels.Length; i++)
            {
                contentPanels[i] = new DarkScrollPanel
                {
                    Location = new Point(0, 0),
                    Size = new Size(panelContent.Width, panelContent.Height),
                    BackColor = Theme.ContentBg,
                    Visible = (i == 0),
                    AutoScroll = true,
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
                };
                panelContent.Controls.Add(contentPanels[i]);
            }

            BuildMainPanel(contentPanels[0]);
            BuildTeleportPanel(contentPanels[1]);
            BuildMiscPanel(contentPanels[2]);
            BuildHotkeysPanel(contentPanels[3]);
        }

        private void BuildMainPanel(Panel panel)
        {
            int y = 20;
            AddSectionHeader(panel, "Connection Status", ref y);

            var lblStatus = CreateLabel("Status:", 20, y);
            panel.Controls.Add(lblStatus);

            lblStatusValue = CreateLabel("NOT CONNECTED", 100, y);
            lblStatusValue.ForeColor = Theme.Danger;
            panel.Controls.Add(lblStatusValue);
            y += 35;

            var lblCcapiIp = CreateLabel("CCAPI Target IP", 20, y);
            panel.Controls.Add(lblCcapiIp);

            txtConsoleIp = CreateTextBox(20, y + 20, 220);
            panel.Controls.Add(txtConsoleIp);

            var btnConnect = CreateButton("Connect (CCAPI)", 250, y + 18, 130, 32);
            btnConnect.Click += btnConnect_Click;
            panel.Controls.Add(btnConnect);
            y += 60;

            var btnStart = CreateButton("Install Hook", 20, y, 110, 32);
            btnStart.Click += btnStart_Click;
            panel.Controls.Add(btnStart);

            var btnUnhook = CreateButton("Unhook", 240, y, 90, 32);
            btnUnhook.Click += btnUnhook_Click;
            panel.Controls.Add(btnUnhook);
            y += 50;

            AddSectionHeader(panel, "Player", ref y);
            AddToggleRow(panel, "God Mode", ref y, out tglGodMode);
            tglGodMode.CheckedChanged += tglGodMode_CheckedChanged;
            AddToggleRow(panel, "Immune", ref y, out tglImmune);
            tglImmune.CheckedChanged += tglImmune_CheckedChanged;
            AddToggleRow(panel, "No Target", ref y, out tglNoTarget);
            tglNoTarget.CheckedChanged += tglNoTarget_CheckedChanged;
            AddToggleRow(panel, "No Collision", ref y, out tglNoCollision);
            tglNoCollision.CheckedChanged += tglNoCollision_CheckedChanged;
            AddToggleRow(panel, "Super Speed", ref y, out tglSuperSpeed);
            tglSuperSpeed.CheckedChanged += tglSuperSpeed_CheckedChanged;

            y += 10;
            AddSectionHeader(panel, "Weapons", ref y);
            AddToggleRow(panel, "Infinite Ammo", ref y, out tglInfiniteAmmo);
            tglInfiniteAmmo.CheckedChanged += tglInfiniteAmmo_CheckedChanged;
            AddToggleRow(panel, "Unlimited Sparrow", ref y, out tglUnlimitedSparrow);
            tglUnlimitedSparrow.CheckedChanged += tglUnlimitedSparrow_CheckedChanged;
            AddToggleRow(panel, "Unlimited Abilities", ref y, out tglUnlimitedAbilities);
            tglUnlimitedAbilities.CheckedChanged += tglUnlimitedAbilities_CheckedChanged;
            AddToggleRow(panel, "No Recoil", ref y, out tglNoRecoil);
            tglNoRecoil.CheckedChanged += tglNoRecoil_CheckedChanged;
            AddToggleRow(panel, "No Spread", ref y, out tglNoSpread);
            tglNoSpread.CheckedChanged += tglNoSpread_CheckedChanged;
            AddToggleRow(panel, "One Hit Kill", ref y, out tglOneHitKill);
            tglOneHitKill.CheckedChanged += tglOneHitKill_CheckedChanged;
            AddToggleRow(panel, "Rapid Fire", ref y, out tglRapidFire);
            tglRapidFire.CheckedChanged += tglRapidFire_CheckedChanged;

            var lblFOV = CreateLabel("FOV", 20, y + 5);
            panel.Controls.Add(lblFOV);
            sliderFOV = CreateSlider(60, y, 160, 0, 9, 0);
            sliderFOV.Scroll += sliderFOV_Scroll;
            panel.Controls.Add(sliderFOV);
            lblFOVValue = CreateLabel("2.00", 230, y + 5);
            panel.Controls.Add(lblFOVValue);
            UpdateFovValue();
            y += 45;

            AddSectionHeader(panel, "World", ref y);
            AddToggleRow(panel, "Enemies To Void", ref y, out tglEnemiesVoid);
            tglEnemiesVoid.CheckedChanged += tglEnemiesVoid_CheckedChanged;
            AddToggleRow(panel, "Instant Revive", ref y, out tglInstantRevive);
            tglInstantRevive.CheckedChanged += tglInstantRevive_CheckedChanged;
            AddToggleRow(panel, "Stop Bullets", ref y, out tglStopBullets);
            tglStopBullets.CheckedChanged += tglStopBullets_CheckedChanged;
            AddToggleRow(panel, "Static Actors", ref y, out tglStaticActors);
            tglStaticActors.CheckedChanged += tglStaticActors_CheckedChanged;
            AddToggleRow(panel, "Game Speed Modifier", ref y, out tglGameSpeed);
            tglGameSpeed.CheckedChanged += tglGameSpeed_CheckedChanged;

            sliderGameSpeed = CreateSlider(20, y, 200, 1, 10, 2);
            sliderGameSpeed.Scroll += sliderGameSpeed_Scroll;
            panel.Controls.Add(sliderGameSpeed);
            lblGameSpeedValue = CreateLabel("2x", 230, y + 5);
            panel.Controls.Add(lblGameSpeedValue);
            UpdateGameSpeedValue();

            y += 50;
            AddSectionHeader(panel, "Quick Actions", ref y);

            var btnEnableAll = CreateButton("Enable All Mods", 20, y, 120, 32);
            btnEnableAll.BackColor = Theme.Success;
            btnEnableAll.Click += BtnEnableAllMods_Click;
            panel.Controls.Add(btnEnableAll);

            var btnDisableAll = CreateButton("Disable All Mods", 150, y, 120, 32);
            btnDisableAll.BackColor = Theme.Danger;
            btnDisableAll.Click += BtnDisableAllMods_Click;
            panel.Controls.Add(btnDisableAll);
        }

        // ═══════════════════════════════════════════════════════════════
        // STATUS PANEL
        // ═══════════════════════════════════════════════════════════════
        private void BuildStatusPanel(Panel panel)
        {
            int y = 20;
            AddSectionHeader(panel, "Connection Status", ref y);

            var lblStatus = CreateLabel("Status:", 20, y);
            panel.Controls.Add(lblStatus);

            lblStatusValue = CreateLabel("NOT CONNECTED", 100, y);
            lblStatusValue.ForeColor = Theme.Danger;
            panel.Controls.Add(lblStatusValue);
            y += 35;

            var btnConnect = CreateButton("Connect", 20, y, 100, 32);
            btnConnect.Click += btnConnect_Click;
            panel.Controls.Add(btnConnect);

            var btnStart = CreateButton("Install Hook", 130, y, 110, 32);
            btnStart.Click += btnStart_Click;
            panel.Controls.Add(btnStart);
            
            var btnUnhook = CreateButton("Unhook", 250, y, 90, 32);
            btnUnhook.Click += btnUnhook_Click;
            panel.Controls.Add(btnUnhook);
            y += 50;
            
            // Quick Actions
            AddSectionHeader(panel, "Quick Actions", ref y);
            
            var btnEnableAll = CreateButton("Enable All Mods", 20, y, 120, 32);
            btnEnableAll.BackColor = Theme.Success;
            btnEnableAll.Click += BtnEnableAllMods_Click;
            panel.Controls.Add(btnEnableAll);
            
            var btnDisableAll = CreateButton("Disable All Mods", 150, y, 120, 32);
            btnDisableAll.BackColor = Theme.Danger;
            btnDisableAll.Click += BtnDisableAllMods_Click;
            panel.Controls.Add(btnDisableAll);
        }

        // ═══════════════════════════════════════════════════════════════
        // PLAYER PANEL - All OverSRC Player Mods
        // ═══════════════════════════════════════════════════════════════
        private void BuildPlayerPanel(Panel panel)
        {
            int y = 20;
            AddSectionHeader(panel, "Player Modifications", ref y);

            AddToggleRow(panel, "God Mode", ref y, out tglGodMode);
            tglGodMode.CheckedChanged += tglGodMode_CheckedChanged;

            AddToggleRow(panel, "Immune", ref y, out tglImmune);
            tglImmune.CheckedChanged += tglImmune_CheckedChanged;

            AddToggleRow(panel, "No Target", ref y, out tglNoTarget);
            tglNoTarget.CheckedChanged += tglNoTarget_CheckedChanged;

            AddToggleRow(panel, "No Collision", ref y, out tglNoCollision);
            tglNoCollision.CheckedChanged += tglNoCollision_CheckedChanged;

            AddToggleRow(panel, "Super Speed", ref y, out tglSuperSpeed);
            tglSuperSpeed.CheckedChanged += tglSuperSpeed_CheckedChanged;
        }

        // ═══════════════════════════════════════════════════════════════
        // WEAPONS PANEL - All OverSRC Weapon Mods
        // ═══════════════════════════════════════════════════════════════
        private void BuildWeaponsPanel(Panel panel)
        {
            int y = 20;
            AddSectionHeader(panel, "Ammo & Abilities", ref y);

            AddToggleRow(panel, "Infinite Ammo", ref y, out tglInfiniteAmmo);
            tglInfiniteAmmo.CheckedChanged += tglInfiniteAmmo_CheckedChanged;

            AddToggleRow(panel, "Unlimited Sparrow", ref y, out tglUnlimitedSparrow);
            tglUnlimitedSparrow.CheckedChanged += tglUnlimitedSparrow_CheckedChanged;

            AddToggleRow(panel, "Unlimited Abilities", ref y, out tglUnlimitedAbilities);
            tglUnlimitedAbilities.CheckedChanged += tglUnlimitedAbilities_CheckedChanged;

            y += 10;
            AddSectionHeader(panel, "Weapon Modifications", ref y);

            AddToggleRow(panel, "No Recoil", ref y, out tglNoRecoil);
            tglNoRecoil.CheckedChanged += tglNoRecoil_CheckedChanged;
            
            AddToggleRow(panel, "No Spread", ref y, out tglNoSpread);
            tglNoSpread.CheckedChanged += tglNoSpread_CheckedChanged;

            AddToggleRow(panel, "One Hit Kill", ref y, out tglOneHitKill);
            tglOneHitKill.CheckedChanged += tglOneHitKill_CheckedChanged;

            AddToggleRow(panel, "Rapid Fire", ref y, out tglRapidFire);
            tglRapidFire.CheckedChanged += tglRapidFire_CheckedChanged;

            y += 10;
            AddSectionHeader(panel, "Field of View", ref y);

            sliderFOV = CreateSlider(20, y, 200, 0, 100, 0);
            sliderFOV.Scroll += sliderFOV_Scroll;
            panel.Controls.Add(sliderFOV);

            lblFOVValue = CreateLabel("2.00", 230, y + 5);
            panel.Controls.Add(lblFOVValue);
            UpdateFovValue();
        }

        // ═══════════════════════════════════════════════════════════════
        // TELEPORT PANEL
        // ═══════════════════════════════════════════════════════════════
        private void BuildTeleportPanel(Panel panel)
        {
            teleportPanel = panel;
            int y = 20;
            
            AddSectionHeader(panel, "Teleport to Selected Hotkey", ref y);
            
            var lblHotkeyDesc = CreateLabel("Hotkey:", 20, y + 3);
            panel.Controls.Add(lblHotkeyDesc);
            
            lblTeleportHotkey = CreateLabel("None", 80, y + 3);
            lblTeleportHotkey.ForeColor = Theme.Accent;
            panel.Controls.Add(lblTeleportHotkey);
            
            var btnBindTeleportKey = CreateButton("Bind", 160, y, 60, 26);
            btnBindTeleportKey.Click += BtnBindTeleportKey_Click;
            panel.Controls.Add(btnBindTeleportKey);
            
            var lblArrowTip = CreateLabel("(Use Arrow UP/DOWN to select)", 230, y + 3);
            lblArrowTip.ForeColor = Theme.TextDim;
            panel.Controls.Add(lblArrowTip);
            y += 40;

            AddSectionHeader(panel, "Position Actions", ref y);
            
            var btnAddCurrent = CreateButton("Add Current", 20, y, 100, 28);
            btnAddCurrent.Click += BtnAddCurrentPosition_Click;
            panel.Controls.Add(btnAddCurrent);

            var btnImport = CreateButton("Import", 130, y, 70, 28);
            btnImport.Click += BtnImportPositions_Click;
            panel.Controls.Add(btnImport);

            var btnExport = CreateButton("Export", 210, y, 70, 28);
            btnExport.Click += BtnExportPositions_Click;
            panel.Controls.Add(btnExport);

            var btnClearAll = CreateButton("Clear All", 290, y, 80, 28);
            btnClearAll.BackColor = Theme.Danger;
            btnClearAll.FlatAppearance.MouseOverBackColor = Theme.DangerHover;
            btnClearAll.Click += BtnClearAllPositions_Click;
            panel.Controls.Add(btnClearAll);
            
            var btnTrueDeath = CreateButton("True Death", 380, y, 90, 28);
            btnTrueDeath.BackColor = Color.FromArgb(80, 40, 40);
            btnTrueDeath.Click += BtnTrueDeath_Click;
            panel.Controls.Add(btnTrueDeath);
            
            var btnLaunch = CreateButton("Launch Up", 480, y, 90, 28);
            btnLaunch.Click += BtnLaunchUp_Click;
            panel.Controls.Add(btnLaunch);
            y += 45;

            AddSectionHeader(panel, "Saved Positions", ref y);

            lvPositions = new ListView
            {
                Location = new Point(20, y),
                Size = new Size(620, 180),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Theme.InputBg,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f),
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                OwnerDraw = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lvPositions.Columns.Add("Name", 120);
            lvPositions.Columns.Add("X", 80);
            lvPositions.Columns.Add("Y", 80);
            lvPositions.Columns.Add("Z", 80);
            lvPositions.Columns.Add("Hotkey", 120);
            lvPositions.SelectedIndexChanged += LvPositions_SelectedIndexChanged;
            lvPositions.DoubleClick += LvPositions_DoubleClick;
            lvPositions.DrawColumnHeader += LvPositions_DrawColumnHeader;
            lvPositions.DrawItem += LvPositions_DrawItem;
            lvPositions.DrawSubItem += LvPositions_DrawSubItem;
            panel.Controls.Add(lvPositions);
            panel.SizeChanged += (s, e) => UpdateTeleportListLayout();
            UpdateTeleportListLayout();
            y += 190;

            var btnTeleportSelected = CreateButton("Teleport", 20, y, 80, 28);
            btnTeleportSelected.Click += BtnTeleportSelected_Click;
            panel.Controls.Add(btnTeleportSelected);

            var btnSetHotkey = CreateButton("Set Hotkey", 110, y, 90, 28);
            btnSetHotkey.Click += BtnSetHotkey_Click;
            panel.Controls.Add(btnSetHotkey);

            var btnClearHotkey = CreateButton("Clear Hotkey", 210, y, 100, 28);
            btnClearHotkey.Click += BtnClearHotkey_Click;
            panel.Controls.Add(btnClearHotkey);

            var btnDeleteSelected = CreateButton("Delete", 320, y, 70, 28);
            btnDeleteSelected.BackColor = Theme.Danger;
            btnDeleteSelected.FlatAppearance.MouseOverBackColor = Theme.DangerHover;
            btnDeleteSelected.Click += BtnDeleteSelected_Click;
            panel.Controls.Add(btnDeleteSelected);
            y += 45;

            AddSectionHeader(panel, "Manual Teleport", ref y);

            var lblTeleX = CreateLabel("X:", 20, y + 3);
            panel.Controls.Add(lblTeleX);
            txtTeleX = CreateTextBox(40, y, 80);
            panel.Controls.Add(txtTeleX);

            var lblTeleY = CreateLabel("Y:", 135, y + 3);
            panel.Controls.Add(lblTeleY);
            txtTeleY = CreateTextBox(155, y, 80);
            panel.Controls.Add(txtTeleY);

            var lblTeleZ = CreateLabel("Z:", 250, y + 3);
            panel.Controls.Add(lblTeleZ);
            txtTeleZ = CreateTextBox(270, y, 80);
            panel.Controls.Add(txtTeleZ);

            var btnTeleport = CreateButton("Teleport", 365, y, 80, 26);
            btnTeleport.Click += btnTeleport_Click;
            panel.Controls.Add(btnTeleport);

            var btnGetCoords = CreateButton("Get Coords", 455, y, 90, 26);
            btnGetCoords.Click += btnGetCoords_Click;
            panel.Controls.Add(btnGetCoords);
        }

        // ═══════════════════════════════════════════════════════════════
        // WORLD PANEL - All OverSRC World Mods
        // ═══════════════════════════════════════════════════════════════
        private void BuildWorldPanel(Panel panel)
        {
            int y = 20;
            AddSectionHeader(panel, "World Modifications", ref y);

            AddToggleRow(panel, "Enemies To Void", ref y, out tglEnemiesVoid);
            tglEnemiesVoid.CheckedChanged += tglEnemiesVoid_CheckedChanged;

            AddToggleRow(panel, "Instant Revive", ref y, out tglInstantRevive);
            tglInstantRevive.CheckedChanged += tglInstantRevive_CheckedChanged;

            AddToggleRow(panel, "Stop Bullets", ref y, out tglStopBullets);
            tglStopBullets.CheckedChanged += tglStopBullets_CheckedChanged;

            AddToggleRow(panel, "Static Actors", ref y, out tglStaticActors);
            tglStaticActors.CheckedChanged += tglStaticActors_CheckedChanged;

            y += 10;
            AddSectionHeader(panel, "Game Speed", ref y);
            
            AddToggleRow(panel, "Game Speed Modifier", ref y, out tglGameSpeed);
            tglGameSpeed.CheckedChanged += tglGameSpeed_CheckedChanged;

            sliderGameSpeed = CreateSlider(20, y, 200, 1, 10, 2);
            sliderGameSpeed.Scroll += sliderGameSpeed_Scroll;
            panel.Controls.Add(sliderGameSpeed);

            lblGameSpeedValue = CreateLabel("2x", 230, y + 5);
            panel.Controls.Add(lblGameSpeedValue);
            UpdateGameSpeedValue();
        }

        // ═══════════════════════════════════════════════════════════════
        // MISC PANEL
        // ═══════════════════════════════════════════════════════════════
        private void BuildMiscPanel(Panel panel)
        {
            int y = 20;
            AddSectionHeader(panel, "Identity", ref y);

            var lblName = CreateLabel("Name:", 20, y + 3);
            panel.Controls.Add(lblName);
            txtIdentityName = CreateTextBox(80, y, 180);
            panel.Controls.Add(txtIdentityName);
            y += 35;

            var lblEmblem = CreateLabel("Emblem:", 20, y + 3);
            panel.Controls.Add(lblEmblem);

            cmbEmblemId = new ComboBox
            {
                Location = new Point(80, y),
                Size = new Size(260, 24),
                Font = new Font("Segoe UI", 9f),
                BackColor = Theme.InputBg,
                ForeColor = Theme.Text,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            
            foreach (var emblem in EmblemList.Emblems)
            {
                cmbEmblemId.Items.Add(emblem);
            }
            
            panel.Controls.Add(cmbEmblemId);
            y += 40;

            var btnGetInfo = CreateButton("Get Info", 20, y, 90, 28);
            btnGetInfo.Click += BtnGetIdentityInfo_Click;
            panel.Controls.Add(btnGetInfo);

            var btnSetInfo = CreateButton("Set Info", 120, y, 90, 28);
            btnSetInfo.Click += BtnSetIdentityInfo_Click;
            panel.Controls.Add(btnSetInfo);
            y += 50;

            AddSectionHeader(panel, "Activity Manager", ref y);

            var lblWarning1 = CreateLabel("Note: Loader will not work after joining another fireteam.", 20, y);
            lblWarning1.ForeColor = Theme.TextDim;
            panel.Controls.Add(lblWarning1);
            y += 20;

            var lblWarning2 = CreateLabel("Warning: Invalid activity IDs can cause a crash.", 20, y);
            lblWarning2.ForeColor = Theme.Danger;
            panel.Controls.Add(lblWarning2);
            y += 30;

            var lblActivityIndex = CreateLabel("Activity:", 20, y + 3);
            panel.Controls.Add(lblActivityIndex);

            cmbActivityIndex = new ComboBox
            {
                Location = new Point(80, y),
                Size = new Size(260, 24),
                Font = new Font("Segoe UI", 9f),
                BackColor = Theme.InputBg,
                ForeColor = Theme.Text,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            panel.Controls.Add(cmbActivityIndex);
            y += 35;

            btnReadActivity = CreateButton("Read", 20, y, 90, 28);
            btnReadActivity.Click += BtnReadActivity_Click;
            panel.Controls.Add(btnReadActivity);

            btnWriteActivity = CreateButton("Write", 120, y, 90, 28);
            btnWriteActivity.Click += BtnWriteActivity_Click;
            panel.Controls.Add(btnWriteActivity);
            y += 50;
            
            // Quick Activity Loaders
            AddSectionHeader(panel, "Quick Activities", ref y);
            
            var btnOrbit = CreateButton("Orbit", 20, y, 80, 28);
            btnOrbit.Click += (s, e) => SelectActivity(125);
            panel.Controls.Add(btnOrbit);
            
            var btnTower = CreateButton("Tower", 110, y, 80, 28);
            btnTower.Click += (s, e) => SelectActivity(138);
            panel.Controls.Add(btnTower);
            
            var btnReef = CreateButton("Reef", 200, y, 80, 28);
            btnReef.Click += (s, e) => SelectActivity(326);
            panel.Controls.Add(btnReef);
            
            var btnRandom = CreateButton("Random Crucible", 290, y, 120, 28);
            btnRandom.Click += (s, e) => LoadActivity((ushort)new Random().Next(2, 8));
            panel.Controls.Add(btnRandom);
        }

        // ═══════════════════════════════════════════════════════════════
        // HOTKEYS PANEL
        // ═══════════════════════════════════════════════════════════════
        private void BuildHotkeysPanel(Panel panel)
        {
            int y = 20;
            AddSectionHeader(panel, "Feature Hotkeys", ref y);

            var lblTip = CreateLabel("Assign hotkeys to trigger any feature instantly.", 20, y);
            lblTip.ForeColor = Theme.TextDim;
            panel.Controls.Add(lblTip);
            y += 25;

            lvFeatureHotkeys = new ListView
            {
                Location = new Point(20, y),
                Size = new Size(620, 240),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Theme.InputBg,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f),
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lvFeatureHotkeys.Columns.Add("Feature", 240);
            lvFeatureHotkeys.Columns.Add("Category", 140);
            lvFeatureHotkeys.Columns.Add("Hotkey", 120);
            lvFeatureHotkeys.SelectedIndexChanged += LvFeatureHotkeys_SelectedIndexChanged;
            panel.Controls.Add(lvFeatureHotkeys);
            panel.SizeChanged += (s, e) => UpdateFeatureHotkeyListLayout();
            UpdateFeatureHotkeyListLayout();
            y += 250;

            lblSelectedHotkeyFeature = CreateLabel("Selected: None", 20, y + 5);
            lblSelectedHotkeyFeature.ForeColor = Theme.TextDim;
            panel.Controls.Add(lblSelectedHotkeyFeature);

            var btnSetFeatureHotkey = CreateButton("Set Hotkey", 320, y, 100, 28);
            btnSetFeatureHotkey.Click += BtnSetFeatureHotkey_Click;
            panel.Controls.Add(btnSetFeatureHotkey);

            var btnClearFeatureHotkey = CreateButton("Clear Hotkey", 430, y, 100, 28);
            btnClearFeatureHotkey.Click += BtnClearFeatureHotkey_Click;
            panel.Controls.Add(btnClearFeatureHotkey);

            var btnClearAllHotkeys = CreateButton("Clear All", 540, y, 100, 28);
            btnClearAllHotkeys.BackColor = Theme.Danger;
            btnClearAllHotkeys.FlatAppearance.MouseOverBackColor = Theme.DangerHover;
            btnClearAllHotkeys.Click += BtnClearAllFeatureHotkeys_Click;
            panel.Controls.Add(btnClearAllHotkeys);
        }

        // ═══════════════════════════════════════════════════════════════
        // SETTINGS PANEL
        // ═══════════════════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════════════════
        // UI HELPER METHODS
        // ═══════════════════════════════════════════════════════════════

        private void AddSectionHeader(Panel panel, string text, ref int y)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI Semibold", 10f),
                ForeColor = Theme.Text,
                Location = new Point(10, y),
                AutoSize = true
            };
            panel.Controls.Add(lbl);

            var line = new Panel
            {
                Location = new Point(10, y + 22),
                Size = new Size(panel.Width - 50, 1),
                BackColor = Theme.CardBorder,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            panel.Controls.Add(line);

            y += 35;
        }

        private void AddToggleRow(Panel panel, string text, ref int y, out ToggleSwitch toggle)
        {
            var lbl = CreateLabel(text, 20, y + 2);
            panel.Controls.Add(lbl);

            toggle = new ToggleSwitch
            {
                Location = new Point(200, y),
                BackColor = Theme.Background
            };
            panel.Controls.Add(toggle);

            y += 32;
        }

        private Label CreateLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Theme.Text,
                Location = new Point(x, y),
                AutoSize = true
            };
        }

        private Button CreateButton(string text, int x, int y, int width, int height)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.CardBg,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Theme.CardBorder;
            btn.FlatAppearance.MouseOverBackColor = Theme.TabHover;
            return btn;
        }

        private TextBox CreateTextBox(int x, int y, int width)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 24),
                BackColor = Theme.InputBg,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
        }

        private TrackBar CreateSlider(int x, int y, int width, int min, int max, int value)
        {
            return new TrackBar
            {
                Location = new Point(x, y),
                Size = new Size(width, 30),
                Minimum = min,
                Maximum = max,
                Value = value,
                TickStyle = TickStyle.None,
                BackColor = Theme.Background
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // FORM PAINT
        // ═══════════════════════════════════════════════════════════════
        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw title bar background to match sidebar/menu palette
            using (var brush = new SolidBrush(Theme.LeftPanel))
            {
                e.Graphics.FillRectangle(brush, 0, 0, this.Width, 38);
            }

            // Draw main border
            using (var pen = new Pen(Theme.CardBorder, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }

            // Draw separator line below title bar
            using (var pen = new Pen(Theme.CardBorder, 1))
            {
                int sidebarWidth = panelLeft?.Width ?? 161;
                e.Graphics.DrawLine(pen, sidebarWidth, 38, this.Width, 38);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayoutSizing();
        }

        private void UpdateLayoutSizing()
        {
            if (panelLeft == null || panelContent == null) return;

            int sidebarWidth = 161;
            panelLeft.Width = sidebarWidth;
            panelLeft.Height = this.Height - 38;
            panelContent.Location = new Point(sidebarWidth, 38);
            panelContent.Size = new Size(this.Width - sidebarWidth, this.Height - 38);

            btnClose.Location = new Point(this.Width - 35, 7);
            btnMin.Location = new Point(this.Width - 65, 7);

            lblLogo.Size = new Size(panelLeft.Width - 20, lblLogo.Height);

            lblUserInfo.Location = new Point(10, panelLeft.Height - 55);
            lblLicenseInfo.Location = new Point(10, panelLeft.Height - 35);

            if (tabButtons != null)
            {
                int tabWidth = Math.Max(110, panelLeft.Width - 26);
                foreach (var tab in tabButtons)
                {
                    tab.Width = tabWidth;
                }
            }

            if (contentPanels != null)
            {
                foreach (var panel in contentPanels)
                {
                    panel.Size = new Size(panelContent.Width, panelContent.Height);
                }
            }

            UpdateTeleportListLayout();
            UpdateFeatureHotkeyListLayout();
            Invalidate();
        }

        private void UpdateTeleportListLayout()
        {
            if (teleportPanel == null || lvPositions == null) return;

            int listWidth = Math.Max(320, teleportPanel.Width - 60);
            lvPositions.Width = listWidth;

            int remaining = listWidth - 2;
            int nameWidth = Math.Min(160, remaining / 3);
            int coordWidth = 90;
            int hotkeyWidth = Math.Max(120, remaining - (nameWidth + (coordWidth * 3)));
            if (hotkeyWidth < 120)
            {
                coordWidth = Math.Max(70, (remaining - nameWidth - 120) / 3);
                hotkeyWidth = remaining - (nameWidth + (coordWidth * 3));
            }

            lvPositions.Columns[0].Width = nameWidth;
            lvPositions.Columns[1].Width = coordWidth;
            lvPositions.Columns[2].Width = coordWidth;
            lvPositions.Columns[3].Width = coordWidth;
            lvPositions.Columns[4].Width = hotkeyWidth;
        }

        private void UpdateFeatureHotkeyListLayout()
        {
            if (lvFeatureHotkeys == null) return;

            int listWidth = Math.Max(320, lvFeatureHotkeys.Parent.Width - 60);
            lvFeatureHotkeys.Width = listWidth;

            int remaining = listWidth - 2;
            int featureWidth = Math.Min(260, (int)(remaining * 0.5));
            int categoryWidth = Math.Min(160, (int)(remaining * 0.25));
            int hotkeyWidth = Math.Max(120, remaining - featureWidth - categoryWidth);

            lvFeatureHotkeys.Columns[0].Width = featureWidth;
            lvFeatureHotkeys.Columns[1].Width = categoryWidth;
            lvFeatureHotkeys.Columns[2].Width = hotkeyWidth;
        }

        private void InitializeFeatureHotkeys()
        {
            featureHotkeys.Clear();
            featureHotkeyMap.Clear();

            RegisterFeatureHotkey("Connect", "Status", () => btnConnect_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Install Hook", "Status", () => btnStart_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Unhook", "Status", () => btnUnhook_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Enable All Mods", "Status", () => BtnEnableAllMods_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Disable All Mods", "Status", () => BtnDisableAllMods_Click(this, EventArgs.Empty));

            RegisterFeatureHotkey("God Mode", "Player", () => tglGodMode.Checked = !tglGodMode.Checked);
            RegisterFeatureHotkey("Immune", "Player", () => tglImmune.Checked = !tglImmune.Checked);
            RegisterFeatureHotkey("No Target", "Player", () => tglNoTarget.Checked = !tglNoTarget.Checked);
            RegisterFeatureHotkey("No Collision", "Player", () => tglNoCollision.Checked = !tglNoCollision.Checked);
            RegisterFeatureHotkey("Super Speed", "Player", () => tglSuperSpeed.Checked = !tglSuperSpeed.Checked);

            RegisterFeatureHotkey("Infinite Ammo", "Weapons", () => tglInfiniteAmmo.Checked = !tglInfiniteAmmo.Checked);
            RegisterFeatureHotkey("Unlimited Sparrow", "Weapons", () => tglUnlimitedSparrow.Checked = !tglUnlimitedSparrow.Checked);
            RegisterFeatureHotkey("Unlimited Abilities", "Weapons", () => tglUnlimitedAbilities.Checked = !tglUnlimitedAbilities.Checked);
            RegisterFeatureHotkey("No Recoil", "Weapons", () => tglNoRecoil.Checked = !tglNoRecoil.Checked);
            RegisterFeatureHotkey("No Spread", "Weapons", () => tglNoSpread.Checked = !tglNoSpread.Checked);
            RegisterFeatureHotkey("One Hit Kill", "Weapons", () => tglOneHitKill.Checked = !tglOneHitKill.Checked);
            RegisterFeatureHotkey("Rapid Fire", "Weapons", () => tglRapidFire.Checked = !tglRapidFire.Checked);
            RegisterFeatureHotkey("Increase FOV", "Weapons", () => AdjustFov(1));
            RegisterFeatureHotkey("Decrease FOV", "Weapons", () => AdjustFov(-1));

            RegisterFeatureHotkey("Teleport to Selected", "Teleport", () => BtnTeleportSelected_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Add Current Position", "Teleport", () => BtnAddCurrentPosition_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Import Positions", "Teleport", () => BtnImportPositions_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Export Positions", "Teleport", () => BtnExportPositions_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Clear All Positions", "Teleport", () => BtnClearAllPositions_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("True Death", "Teleport", () => BtnTrueDeath_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Launch Up", "Teleport", () => BtnLaunchUp_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Set Position Hotkey", "Teleport", () => BtnSetHotkey_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Clear Position Hotkey", "Teleport", () => BtnClearHotkey_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Delete Position", "Teleport", () => BtnDeleteSelected_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Manual Teleport", "Teleport", () => btnTeleport_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Get Coords", "Teleport", () => btnGetCoords_Click(this, EventArgs.Empty));

            RegisterFeatureHotkey("Enemies to Void", "World", () => tglEnemiesVoid.Checked = !tglEnemiesVoid.Checked);
            RegisterFeatureHotkey("Instant Revive", "World", () => tglInstantRevive.Checked = !tglInstantRevive.Checked);
            RegisterFeatureHotkey("Stop Bullets", "World", () => tglStopBullets.Checked = !tglStopBullets.Checked);
            RegisterFeatureHotkey("Static Actors", "World", () => tglStaticActors.Checked = !tglStaticActors.Checked);
            RegisterFeatureHotkey("Game Speed Modifier", "World", () => tglGameSpeed.Checked = !tglGameSpeed.Checked);
            RegisterFeatureHotkey("Increase Game Speed", "World", () => AdjustGameSpeed(1));
            RegisterFeatureHotkey("Decrease Game Speed", "World", () => AdjustGameSpeed(-1));

            RegisterFeatureHotkey("Get Identity", "Misc", () => BtnGetIdentityInfo_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Set Identity", "Misc", () => BtnSetIdentityInfo_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Read Activity", "Misc", () => BtnReadActivity_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Write Activity", "Misc", () => BtnWriteActivity_Click(this, EventArgs.Empty));
            RegisterFeatureHotkey("Load Orbit", "Misc", () => LoadActivity(125));
            RegisterFeatureHotkey("Load Tower", "Misc", () => LoadActivity(138));
            RegisterFeatureHotkey("Load Reef", "Misc", () => LoadActivity(326));
            RegisterFeatureHotkey("Random Crucible", "Misc", () => LoadActivity((ushort)new Random().Next(2, 8)));

            RefreshFeatureHotkeyList();
        }

        private void RegisterFeatureHotkey(string name, string category, Action action)
        {
            featureHotkeys.Add(new FeatureHotkey(name, category, action));
        }

        private void RefreshFeatureHotkeyList()
        {
            if (lvFeatureHotkeys == null) return;

            lvFeatureHotkeys.BeginUpdate();
            lvFeatureHotkeys.Items.Clear();
            foreach (var feature in featureHotkeys)
            {
                var item = new ListViewItem(feature.Name) { Tag = feature };
                item.SubItems.Add(feature.Category);
                item.SubItems.Add(feature.Hotkey == Keys.None ? "None" : feature.Hotkey.ToString());
                lvFeatureHotkeys.Items.Add(item);
            }
            lvFeatureHotkeys.EndUpdate();
        }

        private void AssignFeatureHotkey(FeatureHotkey feature, Keys newHotkey)
        {
            if (feature == null) return;

            if (newHotkey != Keys.None)
            {
                if (hotkeyMap.ContainsKey(newHotkey))
                {
                    MessageBox.Show($"Hotkey {newHotkey} is already used by a saved position.", "Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (teleportToSelectedHotkey == newHotkey)
                {
                    MessageBox.Show($"Hotkey {newHotkey} is already used for teleport-to-selected.", "Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (featureHotkeyMap.TryGetValue(newHotkey, out var existingFeature) && existingFeature != feature)
                {
                    existingFeature.Hotkey = Keys.None;
                }
            }

            if (feature.Hotkey != Keys.None)
            {
                featureHotkeyMap.Remove(feature.Hotkey);
            }

            feature.Hotkey = newHotkey;

            if (newHotkey != Keys.None)
            {
                featureHotkeyMap[newHotkey] = feature;
            }

            RefreshFeatureHotkeyList();
            SaveHotkeyPreferences();
        }

        private void LvFeatureHotkeys_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvFeatureHotkeys.SelectedItems.Count == 0)
            {
                selectedFeatureHotkey = null;
                lblSelectedHotkeyFeature.Text = "Selected: None";
                return;
            }

            selectedFeatureHotkey = lvFeatureHotkeys.SelectedItems[0].Tag as FeatureHotkey;
            lblSelectedHotkeyFeature.Text = selectedFeatureHotkey == null
                ? "Selected: None"
                : $"Selected: {selectedFeatureHotkey.Name}";
        }

        private void BtnSetFeatureHotkey_Click(object sender, EventArgs e)
        {
            if (selectedFeatureHotkey == null)
            {
                MessageBox.Show("Select a feature first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var hotkeyForm = new HotkeyInputForm(selectedFeatureHotkey.Hotkey);
            if (hotkeyForm.ShowDialog() == DialogResult.OK)
            {
                AssignFeatureHotkey(selectedFeatureHotkey, hotkeyForm.SelectedHotkey);
            }
        }

        private void BtnClearFeatureHotkey_Click(object sender, EventArgs e)
        {
            if (selectedFeatureHotkey == null) return;
            AssignFeatureHotkey(selectedFeatureHotkey, Keys.None);
        }

        private void BtnClearAllFeatureHotkeys_Click(object sender, EventArgs e)
        {
            foreach (var feature in featureHotkeys)
            {
                feature.Hotkey = Keys.None;
            }

            featureHotkeyMap.Clear();
            RefreshFeatureHotkeyList();
            SaveHotkeyPreferences();
        }

        private void SaveHotkeyPreferences()
        {
            try
            {
                var lines = new List<string>
                {
                    $"teleport={teleportToSelectedHotkey}"
                };

                foreach (var feature in featureHotkeys)
                {
                    lines.Add($"feature={feature.Name}|{feature.Hotkey}");
                }

                File.WriteAllLines(hotkeyPrefsPath, lines);
            }
            catch { }
        }

        private void LoadHotkeyPreferences()
        {
            try
            {
                if (!File.Exists(hotkeyPrefsPath)) return;

                foreach (var line in File.ReadAllLines(hotkeyPrefsPath))
                {
                    if (line.StartsWith("teleport=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse(line.Substring(9), out Keys key))
                        {
                            teleportToSelectedHotkey = key;
                        }

                        continue;
                    }

                    if (!line.StartsWith("feature=", StringComparison.OrdinalIgnoreCase)) continue;

                    var payload = line.Substring(8);
                    var parts = payload.Split('|');
                    if (parts.Length != 2) continue;

                    var feature = featureHotkeys.FirstOrDefault(f => f.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    if (feature == null) continue;

                    if (Enum.TryParse(parts[1], out Keys featureKey))
                    {
                        AssignFeatureHotkey(feature, featureKey);
                    }
                }

                if (lblTeleportHotkey != null)
                {
                    lblTeleportHotkey.Text = teleportToSelectedHotkey == Keys.None ? "None" : teleportToSelectedHotkey.ToString();
                }
            }
            catch { }
        }

        private void AdjustFov(int delta)
        {
            if (sliderFOV == null) return;
            sliderFOV.Value = Math.Max(sliderFOV.Minimum, Math.Min(sliderFOV.Maximum, sliderFOV.Value + delta));
            UpdateFovValue();
        }

        private void AdjustGameSpeed(int delta)
        {
            if (sliderGameSpeed == null) return;
            sliderGameSpeed.Value = Math.Max(sliderGameSpeed.Minimum, Math.Min(sliderGameSpeed.Maximum, sliderGameSpeed.Value + delta));
            UpdateGameSpeedValue();
        }

        private void UpdateFovValue()
        {
            if (sliderFOV == null || lblFOVValue == null) return;
            float fov = 2f + sliderFOV.Value * 0.1f;
            lblFOVValue.Text = fov.ToString("F2");
            if (isConnected)
            {
                api.Extension.WriteFloat(7377004U, fov);
            }
        }

        private void UpdateGameSpeedValue()
        {
            if (sliderGameSpeed == null || lblGameSpeedValue == null) return;
            lblGameSpeedValue.Text = $"{sliderGameSpeed.Value}x";
            if (tglGameSpeed.Checked && isConnected)
            {
                api.Extension.WriteFloat(811100260U, (float)sliderGameSpeed.Value);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 0x1;
            const int HTLEFT = 0xA;
            const int HTRIGHT = 0xB;
            const int HTTOP = 0xC;
            const int HTTOPLEFT = 0xD;
            const int HTTOPRIGHT = 0xE;
            const int HTBOTTOM = 0xF;
            const int HTBOTTOMLEFT = 0x10;
            const int HTBOTTOMRIGHT = 0x11;
            const int resizeBorder = 6;

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    Point cursor = PointToClient(new Point(m.LParam.ToInt32()));
                    bool left = cursor.X <= resizeBorder;
                    bool right = cursor.X >= ClientSize.Width - resizeBorder;
                    bool top = cursor.Y <= resizeBorder;
                    bool bottom = cursor.Y >= ClientSize.Height - resizeBorder;

                    if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                    else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }

            base.WndProc(ref m);
        }

        // ═══════════════════════════════════════════════════════════════
        // IDENTITY - From OverSRC
        // ═══════════════════════════════════════════════════════════════
        private const uint IdentityNameAddress = 843607492U;
        private const uint EmblemIdAddress = 1611435136U;

        private void BtnGetIdentityInfo_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;

            try
            {
                txtIdentityName.Text = api.Extension.ReadString(IdentityNameAddress);

                ushort emblemId = api.Extension.ReadUInt16(EmblemIdAddress);
                
                EmblemList.EmblemItem matchingEmblem = null;
                foreach (var item in cmbEmblemId.Items)
                {
                    if (item is EmblemList.EmblemItem emblem && emblem.Id == emblemId)
                    {
                        matchingEmblem = emblem;
                        break;
                    }
                }
                
                if (matchingEmblem != null)
                    cmbEmblemId.SelectedItem = matchingEmblem;
                else
                {
                    cmbEmblemId.SelectedIndex = -1;
                    cmbEmblemId.Text = emblemId.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch { }
        }

        private void BtnSetIdentityInfo_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;

            try
            {
                api.Extension.WriteString(IdentityNameAddress, Regex.Unescape(txtIdentityName.Text));

                ushort emblemId;
                if (cmbEmblemId.SelectedItem is EmblemList.EmblemItem selectedEmblem)
                {
                    emblemId = selectedEmblem.Id;
                    api.Extension.WriteUInt16(EmblemIdAddress, emblemId);
                }
                else if (TryParseEmblemId(cmbEmblemId.Text, out emblemId))
                {
                    api.Extension.WriteUInt16(EmblemIdAddress, emblemId);
                }
            }
            catch { }
        }

        private static bool TryParseEmblemId(string input, out ushort emblemId)
        {
            emblemId = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string trimmed = input.Trim();
            NumberStyles style = NumberStyles.Integer;
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
                style = NumberStyles.HexNumber;
            }

            return ushort.TryParse(trimmed, style, CultureInfo.InvariantCulture, out emblemId);
        }

        // ═══════════════════════════════════════════════════════════════
        // ACTIVITY LOADER - From OverSRC
        // ═══════════════════════════════════════════════════════════════
        
        private ushort ActivityRead()
        {
            return api.Extension.ReadUInt16(811954340U);
        }

        private void BuildActivityOptions()
        {
            activityOptions.Clear();

            if (!LoadActivityOptionsFromFile())
            {
                LoadFallbackActivityOptions();
            }

            activityOptions.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        private bool LoadActivityOptionsFromFile()
        {
            try
            {
                if (!File.Exists(activityDataPath))
                {
                    return false;
                }

                string json = File.ReadAllText(activityDataPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                // Matches items in the format: {"index": 123, "name": "Some Name"}
                const string pattern = @"\{\s*""index""\s*:\s*(?<index>\d+)\s*,\s*""name""\s*:\s*""(?<name>(?:\\.|[^""])*)""\s*\}";
                var matches = Regex.Matches(json, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                foreach (Match match in matches)
                {
                    if (!match.Success)
                    {
                        continue;
                    }

                    ushort index;
                    if (!ushort.TryParse(match.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    {
                        continue;
                    }

                    string rawName = match.Groups["name"].Value;
                    if (string.IsNullOrWhiteSpace(rawName))
                    {
                        continue;
                    }

                    string name = Regex.Unescape(rawName).Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    activityOptions.Add(new ActivityOption(name, index));
                }

                return activityOptions.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private void LoadFallbackActivityOptions()
        {
            activityOptions.Add(new ActivityOption("Tower", 1));
            activityOptions.Add(new ActivityOption("SRL", 125));
            activityOptions.Add(new ActivityOption("Rumble (The Burning Shrine)", 138));
            activityOptions.Add(new ActivityOption("Rumble (Crossroads)", 148));
            activityOptions.Add(new ActivityOption("The Shadow Thief (Light 260)", 326));
            activityOptions.Add(new ActivityOption("First Orbit Screen after Character selection", 65535));
        }

        private void InitializeActivityCombo()
        {
            if (cmbActivityIndex == null) return;

            BuildActivityOptions();
            
            // Populate the combo box with all activities
            cmbActivityIndex.Items.Clear();
            foreach (var option in activityOptions)
            {
                cmbActivityIndex.Items.Add(option);
            }
            
            SelectActivity(ushort.MaxValue);
        }

        private void SelectActivity(ushort index)
        {
            if (cmbActivityIndex == null) return;

            var option = activityOptions.FirstOrDefault(a => a.Index == index);
            if (option != null)
            {
                cmbActivityIndex.SelectedItem = option;
            }
            else
            {
                cmbActivityIndex.Text = index.ToString(CultureInfo.InvariantCulture);
            }
        }

        private bool TryGetSelectedActivityIndex(out ushort activityIndex)
        {
            activityIndex = 0;

            if (cmbActivityIndex.SelectedItem is ActivityOption selected)
            {
                activityIndex = selected.Index;
                return true;
            }

            var text = cmbActivityIndex.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var option = activityOptions.FirstOrDefault(a => a.Name.Equals(text, StringComparison.OrdinalIgnoreCase) || a.ToString().Equals(text, StringComparison.OrdinalIgnoreCase));
            if (option != null)
            {
                activityIndex = option.Index;
                return true;
            }

            var m = Regex.Match(text, @"(\d+)");
            ushort parsed;
            if (m.Success && ushort.TryParse(m.Groups[1].Value, out parsed))
            {
                activityIndex = parsed;
                return true;
            }

            return ushort.TryParse(text, out activityIndex);
        }

        private void LoadActivity(ushort index)
        {
            api.Extension.WriteUInt16(811953774U, index);
            api.Extension.WriteUInt16(811954340U, index);
            api.Extension.WriteUInt16(812149012U, index);
            api.Extension.WriteUInt16(862764102U, index);
            SelectActivity(index);
        }

        private void BtnReadActivity_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;
            try { SelectActivity(ActivityRead()); }
            catch { }
        }

        private void BtnWriteActivity_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;
            try
            {
                if (TryGetSelectedActivityIndex(out ushort activityIndex))
                {
                    LoadActivity(activityIndex);
                }
            }
            catch { }
        }

        private void LoadSavedCcapiTarget()
        {
            try
            {
                if (txtConsoleIp == null) return;
                if (File.Exists(ccapiTargetPath))
                {
                    txtConsoleIp.Text = File.ReadAllText(ccapiTargetPath).Trim();
                }

                if (string.IsNullOrWhiteSpace(txtConsoleIp.Text))
                {
                    txtConsoleIp.Text = "192.168.1.100";
                }
            }
            catch
            {
                if (txtConsoleIp != null && string.IsNullOrWhiteSpace(txtConsoleIp.Text))
                    txtConsoleIp.Text = "192.168.1.100";
            }
        }

        private void SaveCcapiTarget()
        {
            try
            {
                if (txtConsoleIp == null) return;
                File.WriteAllText(ccapiTargetPath, txtConsoleIp.Text.Trim());
            }
            catch { }
        }

        private bool TryInvokeAny(object target, string[] methodNames, object[] args, out object result)
        {
            result = null;
            if (target == null) return false;

            var methods = target.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            foreach (var methodName in methodNames)
            {
                foreach (var method in methods)
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                        continue;

                    try
                    {
                        result = method.Invoke(target, args);
                        return true;
                    }
                    catch
                    {
                        // try next overload/name
                    }
                }
            }

            return false;
        }

        private bool TryInvokeAnyWithCandidates(object target, string[] methodNames, IEnumerable<object[]> argumentCandidates, out object result)
        {
            result = null;
            if (target == null) return false;

            foreach (var args in argumentCandidates)
            {
                if (TryInvokeAny(target, methodNames, args, out result))
                {
                    return true;
                }
            }

            return false;
        }

        private bool WasAttachSuccessful(object invocationResult)
        {
            if (invocationResult == null) return true;
            if (invocationResult is bool b) return b;
            if (invocationResult is int i) return i >= 0;
            if (invocationResult is uint ui) return ui > 0;
            return true;
        }

        private bool TryAutoAttachDestinyProcess(out string error)
        {
            error = string.Empty;
            object result;

            string[] attachMethods = { "AttachGameProcess", "AttachProcess", "Attach", "AttachToGameProcess", "SetProcess" };
            var candidates = new List<object[]>
            {
                Array.Empty<object>(),
                new object[] { "destiny" },
                new object[] { "Destiny" },
                new object[] { "eboot.bin" },
                new object[] { "EBOOT.BIN" },
                new object[] { "BLES" },
                new object[] { "BLUS" },
                new object[] { 0 },
                new object[] { 1 },
                new object[] { (uint)0 },
                new object[] { (uint)1 }
            };

            bool invoked = TryInvokeAnyWithCandidates(api.Connections, attachMethods, candidates, out result) ||
                           TryInvokeAnyWithCandidates(api, attachMethods, candidates, out result);

            if (!invoked)
            {
                error = "Connected to CCAPI, but could not find a compatible attach method in soulsAPI.dll.";
                return false;
            }

            if (!WasAttachSuccessful(result))
            {
                error = "Connected to CCAPI but failed to attach to the running game process.";
                return false;
            }

            return true;
        }

        private void ForceCcapiModeIfAvailable()
        {
            try
            {
                var asm = api.GetType().Assembly;
                var enumType = asm.GetTypes().FirstOrDefault(t => t.IsEnum && t.Name.IndexOf("SelectAPI", StringComparison.OrdinalIgnoreCase) >= 0);
                object ccapiEnumValue = null;

                if (enumType != null)
                {
                    var names = Enum.GetNames(enumType);
                    var ccapiName = names.FirstOrDefault(n =>
                        n.IndexOf("CCAPI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("ControlConsole", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (!string.IsNullOrEmpty(ccapiName))
                    {
                        ccapiEnumValue = Enum.Parse(enumType, ccapiName);
                    }
                }

                if (ccapiEnumValue != null)
                {
                    object ignored;
                    TryInvokeAny(api, new[] { "SetAPI", "SetApi", "ChangeAPI", "SelectAPI" }, new[] { ccapiEnumValue }, out ignored);
                    TryInvokeAny(api.Connections, new[] { "SetAPI", "SetApi", "ChangeAPI", "SelectAPI" }, new[] { ccapiEnumValue }, out ignored);
                }
                else
                {
                    object ignored;
                    TryInvokeAny(api, new[] { "SetAPI", "SetApi", "ChangeAPI", "SelectAPI" }, new object[] { 1 }, out ignored);
                    TryInvokeAny(api.Connections, new[] { "SetAPI", "SetApi", "ChangeAPI", "SelectAPI" }, new object[] { 1 }, out ignored);
                }
            }
            catch { }
        }

        private bool ConnectCcapiOnly(string ip, out string error)
        {
            error = string.Empty;
            try
            {
                ForceCcapiModeIfAvailable();

                object result;
                bool invoked = TryInvokeAny(
                    api.Connections,
                    new[] { "ConnectTarget", "connectTarget", "Connect", "connect", "CCAPIConnect", "ConnectCCAPI" },
                    new object[] { ip },
                    out result);

                if (!invoked)
                {
                    invoked = TryInvokeAny(
                        api,
                        new[] { "ConnectTarget", "connectTarget", "Connect", "connect", "CCAPIConnect", "ConnectCCAPI" },
                        new object[] { ip },
                        out result);
                }

                if (!invoked)
                {
                    error = "Unable to find a CCAPI connection method in soulsAPI.dll.";
                    return false;
                }

                if (result is bool boolResult && !boolResult)
                {
                    error = "CCAPI rejected the connection request. Verify the target IP and CCAPI plugin state.";
                    return false;
                }

                if (!TryAutoAttachDestinyProcess(out string attachError))
                {
                    error = attachError;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CONNECTION - CCAPI ONLY
        // ═══════════════════════════════════════════════════════════════
        private void btnConnect_Click(object sender, EventArgs e)
        {
            string ip = txtConsoleIp?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("Enter your PS3 CCAPI target IP first.", "Missing IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                lblStatusValue.Text = "CONNECTING...";
                lblStatusValue.ForeColor = Theme.Accent;
                Application.DoEvents();

                if (!ConnectCcapiOnly(ip, out string connectError))
                {
                    throw new Exception(string.IsNullOrWhiteSpace(connectError) ? "Unknown CCAPI connection error." : connectError);
                }

                api.Extension.EnableRPC(4882496U, true);
                SaveCcapiTarget();
                isConnected = true;
                lblStatusValue.Text = "CCAPI CONNECTED + ATTACHED";
                lblStatusValue.ForeColor = Theme.Success;
            }
            catch (Exception ex)
            {
                isConnected = false;
                lblStatusValue.Text = "CONNECTION FAILED";
                lblStatusValue.ForeColor = Theme.Danger;
                MessageBox.Show(
                    "CCAPI connection failed." + Environment.NewLine + Environment.NewLine +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "Make sure:" + Environment.NewLine +
                    "• Console and PC are on the same network" + Environment.NewLine +
                    "• CCAPI is installed and running on the PS3" + Environment.NewLine +
                    "• The target IP is correct",
                    "Connection Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                MessageBox.Show("Connect to PS3 first!", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                lblStatusValue.Text = "INSTALLING...";
                lblStatusValue.ForeColor = Theme.Accent;
                Application.DoEvents();

                // OverSRC Player Hook
                api.setMemory(17665728U, new byte[]
                {
                    60, 96, 1, 144, 96, 99, 235, 176, 249, 195, 255, 216, 249, 227, 255, 208,
                    218, 3, 255, 224, 218, 35, 255, 232, 125, 224, 0, 38, 129, 195, 0, 20,
                    194, 4, 1, 64, 194, 46, 0, 0, 252, 16, 136, 64, 64, 130, 0, 40,
                    194, 4, 1, 68, 194, 46, 0, 4, 252, 16, 136, 64, 64, 130, 0, 24,
                    194, 4, 1, 72, 194, 46, 0, 8, 252, 16, 136, 64, 64, 130, 0, 8,
                    144, 131, 0, 0, 125, 239, 241, 32, 233, 195, 255, 216, 233, 227, 255, 208,
                    202, 3, 255, 224, 202, 35, 255, 232, 48, 100, 1, 64, 60, 128, 1, 10,
                    75, 14, 177, 228
                });
                api.setMemory(17665844U, new byte[164]);
                api.Extension.WriteUInt32(26274756U, 812551512U);
                api.Extension.WriteFloat(26274732U, 1.2f);
                api.Extension.WriteUInt32(1851664U, 1223773616U);

                System.Threading.Thread.Sleep(100);

                hookInstalled = true;
                lblStatusValue.Text = "HOOK ACTIVE";
                lblStatusValue.ForeColor = Theme.Success;

                MessageBox.Show(
                    "Player Hook installed successfully!\n\n" +
                    "• OverSRC Hook Active\n" +
                    "• Player Pointer: 0x0190EBB0 / 26274736\n" +
                    "• Use 'Get Coords' to read positions",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                lblStatusValue.Text = "FAILED";
                lblStatusValue.ForeColor = Theme.Danger;
                MessageBox.Show("Failed to install hook: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void btnUnhook_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;
            try
            {
                api.Extension.WriteUInt32(1851664U, 811860288U);
                hookInstalled = false;
                lblStatusValue.Text = "UNHOOKED";
                lblStatusValue.ForeColor = Theme.TextDim;
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // TELEPORT - Core Functions (OverSRC addresses)
        // ═══════════════════════════════════════════════════════════════
        private void btnTeleport_Click(object sender, EventArgs e)
        {
            try
            {
                uint ptr = api.Extension.ReadUInt32(26274736U);
                if (ptr == 0 || ptr == 0xFFFFFFFFU) return;
                if (!float.TryParse(txtTeleX.Text, out float x) ||
                    !float.TryParse(txtTeleY.Text, out float y) ||
                    !float.TryParse(txtTeleZ.Text, out float z)) return;

                TeleportToCoords(x, y, z);
            }
            catch { }
        }

        private void btnGetCoords_Click(object sender, EventArgs e)
        {
            try
            {
                uint ptr = api.Extension.ReadUInt32(26274736U);
                if (ptr == 0 || ptr == 0xFFFFFFFFU) return;

                txtTeleX.Text = api.Extension.ReadFloat(ptr + 320U).ToString("F4");
                txtTeleY.Text = api.Extension.ReadFloat(ptr + 324U).ToString("F4");
                txtTeleZ.Text = api.Extension.ReadFloat(ptr + 328U).ToString("F4");
            }
            catch { }
        }
        
        private void TeleportToCoords(float x, float y, float z)
        {
            uint ptr = api.Extension.ReadUInt32(26274736U);
            if (ptr == 0 || ptr == 0xFFFFFFFFU) return;
            
            byte[] coords = FloatsToArray(x, y, z);
            api.setMemory(ptr + 320U, coords);
            api.Extension.WriteFloat(ptr + 440U, 1f);
        }

        private void TeleportToPosition(SavedPosition pos)
        {
            try
            {
                TeleportToCoords(pos.X, pos.Y, pos.Z);
            }
            catch { }
        }
        
        private static byte[] FloatsToArray(float x, float y, float z)
        {
            byte[] bytes = new byte[12];
            WriteFloat(bytes, 0, x);
            WriteFloat(bytes, 4, y);
            WriteFloat(bytes, 8, z);
            return bytes;
        }
        
        private static void WriteFloat(byte[] bytes, int offset, float value)
        {
            byte[] floatBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(floatBytes);
            Buffer.BlockCopy(floatBytes, 0, bytes, offset, 4);
        }

        // ═══════════════════════════════════════════════════════════════
        // TELEPORT - UI Events
        // ═══════════════════════════════════════════════════════════════
        
        private void BtnBindTeleportKey_Click(object sender, EventArgs e)
        {
            var hotkeyForm = new HotkeyInputForm(teleportToSelectedHotkey);
            if (hotkeyForm.ShowDialog() == DialogResult.OK)
            {
                Keys newHotkey = hotkeyForm.SelectedHotkey;
                if (newHotkey != Keys.None)
                {
                    if (hotkeyMap.ContainsKey(newHotkey))
                    {
                        MessageBox.Show($"Hotkey {newHotkey} is already used by a saved position.", "Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (featureHotkeyMap.ContainsKey(newHotkey))
                    {
                        MessageBox.Show($"Hotkey {newHotkey} is already assigned to a feature.", "Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                teleportToSelectedHotkey = newHotkey;
                lblTeleportHotkey.Text = teleportToSelectedHotkey == Keys.None ? "None" : teleportToSelectedHotkey.ToString();
                SaveHotkeyPreferences();
            }
        }

        private void BtnAddCurrentPosition_Click(object sender, EventArgs e)
        {
            try
            {
                uint ptr = api.Extension.ReadUInt32(26274736U);
                if (ptr == 0 || ptr == 0xFFFFFFFFU)
                {
                    MessageBox.Show("Cannot read player position. Make sure hook is installed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var pos = new SavedPosition
                {
                    Name = $"Position {savedPositions.Count + 1}",
                    X = api.Extension.ReadFloat(ptr + 320U),
                    Y = api.Extension.ReadFloat(ptr + 324U),
                    Z = api.Extension.ReadFloat(ptr + 328U),
                    Hotkey = Keys.None
                };
                savedPositions.Add(pos);
                
                if (savedPositions.Count == 1)
                    selectedPositionIndex = 0;
                    
                RefreshPositionList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add position: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnImportPositions_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Position Files (*.pos;*.csv)|*.pos;*.csv|All Files (*.*)|*.*";
                ofd.Title = "Import Positions";
                
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var lines = File.ReadAllLines(ofd.FileName);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(',');
                            if (parts.Length >= 4)
                            {
                                var pos = new SavedPosition
                                {
                                    Name = parts[0].Trim(),
                                    X = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
                                    Y = float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
                                    Z = float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                                    Hotkey = Keys.None
                                };
                                savedPositions.Add(pos);
                            }
                        }
                        
                        if (savedPositions.Count > 0 && selectedPositionIndex < 0)
                            selectedPositionIndex = 0;
                            
                        RefreshPositionList();
                        MessageBox.Show($"Imported {lines.Length} positions.", "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Import failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void BtnExportPositions_Click(object sender, EventArgs e)
        {
            if (savedPositions.Count == 0)
            {
                MessageBox.Show("No positions to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Position Files (*.pos)|*.pos|CSV Files (*.csv)|*.csv";
                sfd.Title = "Export Positions";
                sfd.FileName = "positions.pos";
                
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var lines = new List<string>();
                        foreach (var pos in savedPositions)
                        {
                            lines.Add($"{pos.Name},{pos.X.ToString("F4", CultureInfo.InvariantCulture)},{pos.Y.ToString("F4", CultureInfo.InvariantCulture)},{pos.Z.ToString("F4", CultureInfo.InvariantCulture)}");
                        }
                        File.WriteAllLines(sfd.FileName, lines);
                        MessageBox.Show($"Exported {savedPositions.Count} positions.", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Export failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void BtnClearAllPositions_Click(object sender, EventArgs e)
        {
            if (savedPositions.Count == 0) return;
            
            if (MessageBox.Show("Are you sure you want to delete ALL saved positions?\nThis action cannot be undone!", 
                "Confirm Clear All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                hotkeyMap.Clear();
                savedPositions.Clear();
                selectedPositionIndex = -1;
                RefreshPositionList();
            }
        }

        private void BtnTrueDeath_Click(object sender, EventArgs e)
        {
            try
            {
                TeleportToCoords(0f, 10000f, 0f);
            }
            catch { }
        }
        
        private void BtnLaunchUp_Click(object sender, EventArgs e)
        {
            try
            {
                uint ptr = api.Extension.ReadUInt32(26274736U);
                if (ptr == 0 || ptr == 0xFFFFFFFFU) return;
                
                float currentZ = api.Extension.ReadFloat(ptr + 328U);
                api.Extension.WriteFloat(ptr + 328U, currentZ + 40f);
                api.Extension.WriteFloat(ptr + 440U, 1f);
            }
            catch { }
        }

        private void LvPositions_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvPositions.SelectedIndices.Count > 0)
                selectedPositionIndex = lvPositions.SelectedIndices[0];
        }

        private void LvPositions_DoubleClick(object sender, EventArgs e)
        {
            BtnTeleportSelected_Click(sender, e);
        }

        private void LvPositions_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var backgroundBrush = new SolidBrush(Theme.CardBg))
            using (var borderPen = new Pen(Theme.CardBorder))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                e.Graphics.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }

            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, e.Bounds, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void LvPositions_DrawItem(object sender, DrawListViewItemEventArgs e) { }

        private void LvPositions_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool isSelected = e.Item.Selected;
            Color background = isSelected ? Theme.Selected : Theme.InputBg;
            Color textColor = isSelected ? Theme.Text : Theme.TextDim;

            using (var backgroundBrush = new SolidBrush(background))
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

            if (!isSelected)
            {
                using (var dividerPen = new Pen(Theme.CardBorder))
                    e.Graphics.DrawLine(dividerPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvPositions.Font, e.Bounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void BtnTeleportSelected_Click(object sender, EventArgs e)
        {
            if (lvPositions.SelectedIndices.Count == 0) return;
            int idx = lvPositions.SelectedIndices[0];
            if (idx >= 0 && idx < savedPositions.Count)
                TeleportToPosition(savedPositions[idx]);
        }

        private void BtnSetHotkey_Click(object sender, EventArgs e)
        {
            if (lvPositions.SelectedIndices.Count == 0)
            {
                MessageBox.Show("Select a position first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int idx = lvPositions.SelectedIndices[0];
            var pos = savedPositions[idx];
            var hotkeyForm = new HotkeyInputForm(pos.Hotkey);

            if (hotkeyForm.ShowDialog() == DialogResult.OK)
            {
                Keys newHotkey = hotkeyForm.SelectedHotkey;

                if (pos.Hotkey != Keys.None && hotkeyMap.ContainsKey(pos.Hotkey))
                    hotkeyMap.Remove(pos.Hotkey);

                if (newHotkey != Keys.None && hotkeyMap.ContainsKey(newHotkey))
                {
                    MessageBox.Show($"Hotkey {newHotkey} is already in use.", "Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (newHotkey != Keys.None && teleportToSelectedHotkey == newHotkey)
                {
                    MessageBox.Show($"Hotkey {newHotkey} is already used for teleport-to-selected.", "Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (newHotkey != Keys.None && featureHotkeyMap.ContainsKey(newHotkey))
                {
                    MessageBox.Show($"Hotkey {newHotkey} is already assigned to a feature.", "Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                pos.Hotkey = newHotkey;
                if (newHotkey != Keys.None)
                    hotkeyMap[newHotkey] = pos;

                RefreshPositionList();
            }
        }

        private void BtnClearHotkey_Click(object sender, EventArgs e)
        {
            if (lvPositions.SelectedIndices.Count == 0) return;
            
            int idx = lvPositions.SelectedIndices[0];
            var pos = savedPositions[idx];
            
            if (pos.Hotkey != Keys.None && hotkeyMap.ContainsKey(pos.Hotkey))
                hotkeyMap.Remove(pos.Hotkey);
            
            pos.Hotkey = Keys.None;
            RefreshPositionList();
        }

        private void BtnDeleteSelected_Click(object sender, EventArgs e)
        {
            if (lvPositions.SelectedIndices.Count == 0) return;
            
            int idx = lvPositions.SelectedIndices[0];
            var pos = savedPositions[idx];
            
            if (pos.Hotkey != Keys.None && hotkeyMap.ContainsKey(pos.Hotkey))
                hotkeyMap.Remove(pos.Hotkey);
            
            savedPositions.RemoveAt(idx);
            
            if (savedPositions.Count == 0)
                selectedPositionIndex = -1;
            else if (selectedPositionIndex >= savedPositions.Count)
                selectedPositionIndex = savedPositions.Count - 1;
            
            RefreshPositionList();
        }

        private void RefreshPositionList()
        {
            lvPositions.Items.Clear();
            
            for (int i = 0; i < savedPositions.Count; i++)
            {
                var pos = savedPositions[i];
                var item = new ListViewItem(pos.Name);
                item.SubItems.Add(pos.X.ToString("F4"));
                item.SubItems.Add(pos.Y.ToString("F4"));
                item.SubItems.Add(pos.Z.ToString("F4"));
                item.SubItems.Add(pos.Hotkey == Keys.None ? "None" : pos.Hotkey.ToString());
                
                if (i == selectedPositionIndex)
                    item.BackColor = Theme.Selected;
                
                lvPositions.Items.Add(item);
            }
            
            if (selectedPositionIndex >= 0 && selectedPositionIndex < lvPositions.Items.Count)
            {
                lvPositions.Items[selectedPositionIndex].Selected = true;
                lvPositions.Items[selectedPositionIndex].EnsureVisible();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PLAYER MODS - From OverSRC
        // ═══════════════════════════════════════════════════════════════
        private void tglGodMode_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            api.Extension.WriteUInt32(4476064U, tglGodMode.Checked ? 3223389256U : 3223389016U);
        }

        private void tglImmune_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglImmune.Checked)
            {
                api.Extension.WriteUInt32(4476068U, 3225551688U);
                api.Extension.WriteUInt32(25762528U, 1065353216U);
            }
            else
                api.Extension.WriteUInt32(4476068U, 3225485552U);
        }

        private void tglNoTarget_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            uint val = tglNoTarget.Checked ? 3221225472U : 1065353216U;
            api.Extension.WriteUInt32(1599516U, val);
            api.Extension.WriteUInt32(1610212U, val);
        }

        private void tglNoCollision_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            api.Extension.WriteFloat(22005392U, tglNoCollision.Checked ? 0f : 1f);
        }

        private void tglSuperSpeed_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            api.Extension.WriteByte(1742528U, tglSuperSpeed.Checked ? (byte)4 : (byte)0);
        }

        // ═══════════════════════════════════════════════════════════════
        // WEAPON MODS - From OverSRC
        // ═══════════════════════════════════════════════════════════════
        private void tglInfiniteAmmo_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglInfiniteAmmo.Checked)
            {
                api.Extension.WriteUInt32(3213548U, 947922703U);
                api.Extension.WriteUInt32(3212628U, 950019855U);
                api.setMemory(13604268U, new byte[] { 96, 0, 0, 0, 252, 30, 248, 0, 63, 192, 70, 28, 99, 222, 60, 0, 147, 223, 1, 16 });
            }
            else
            {
                api.Extension.WriteUInt32(3213548U, 1673789440U);
                api.Extension.WriteUInt32(3212628U, 1673854976U);
                api.setMemory(13604268U, new byte[] { 99, 221, 0, 0, 64, 130, 0, 8, 59, 160, 0, 1, 128, 127, 0, 28, 211, 223, 1, 16 });
            }
        }

        private void tglUnlimitedSparrow_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            api.Extension.WriteUInt32(5285632U, tglUnlimitedSparrow.Checked ? 1610612736U : 3491954732U);
        }

        private void tglUnlimitedAbilities_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglUnlimitedAbilities.Checked)
            {
                api.Extension.WriteUInt32(2891188U, 1610612736U);
                api.Extension.WriteUInt32(13416936U, 811794433U);
                api.Extension.WriteUInt32(2897660U, 3284336644U);
            }
            else
            {
                api.Extension.WriteUInt32(2891188U, 3493527964U);
                api.Extension.WriteUInt32(13416936U, 811859967U);
                api.Extension.WriteUInt32(2897660U, 3284336640U);
            }
        }

        private void tglNoRecoil_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglNoRecoil.Checked)
            {
                api.Extension.WriteFloat(5265116U, 0f);
                api.Extension.WriteFloat(5266724U, 0f);
                api.Extension.WriteFloat(3202116U, 0f);
            }
            else
            {
                api.Extension.WriteFloat(5265116U, 1f);
                api.Extension.WriteFloat(5266724U, 1f);
                api.Extension.WriteFloat(3202116U, 1f);
            }
        }
        
        private void tglNoSpread_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglNoSpread.Checked)
            {
                api.Extension.WriteUInt32(3199536U, 1036831949U);
            }
            else
            {
                api.Extension.WriteUInt32(3199536U, 1065353216U);
            }
        }

        private void tglOneHitKill_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            api.Extension.WriteUInt16(4449830U, tglOneHitKill.Checked ? (ushort)21626 : (ushort)16256);
        }

        private void tglRapidFire_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglRapidFire.Checked)
            {
                api.Extension.WriteUInt32(3175824U, 1240736768U);
                return;
            }
            api.Extension.WriteUInt32(3175824U, 1065353216U);
        }

        private void sliderFOV_Scroll(object sender, EventArgs e)
        {
            UpdateFovValue();
        }

        // ═══════════════════════════════════════════════════════════════
        // WORLD MODS - From OverSRC (Enemies to Void + Game Speed)
        // ═══════════════════════════════════════════════════════════════
        private void tglEnemiesVoid_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected || !hookInstalled)
            {
                if (tglEnemiesVoid.Checked)
                {
                    tglEnemiesVoid.Checked = false;
                    MessageBox.Show("Connect to PS3 and install hook first!", "Not Ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }
            
            // Install the enemies to void hook from OverSRC
            if (tglEnemiesVoid.Checked)
            {
                api.setMemory(17665728U, new byte[]
                {
                    60, 96, 1, 144, 96, 99, 235, 176, 248, 131, 255, 200, 248, 163, 255, 192,
                    248, 195, 255, 184, 249, 195, 255, 216, 249, 227, 255, 208, 218, 3, 255, 224,
                    218, 35, 255, 232, 210, 67, 255, 180, 210, 99, 255, 176, 125, 224, 0, 38,
                    194, 99, 255, 252, 129, 195, 0, 20, 194, 4, 1, 64, 194, 46, 0, 0,
                    238, 80, 136, 40, 254, 64, 146, 16, 252, 18, 152, 64, 65, 129, 0, 56,
                    194, 4, 1, 68, 194, 46, 0, 4, 238, 80, 136, 40, 254, 64, 146, 16,
                    252, 18, 152, 64, 65, 129, 0, 32, 194, 4, 1, 72, 194, 46, 0, 8,
                    238, 80, 136, 40, 254, 64, 146, 16, 252, 18, 152, 64, 65, 129, 0, 8,
                    144, 131, 0, 0, 194, 46, 0, 0, 194, 4, 1, 64, 238, 80, 136, 40,
                    254, 64, 146, 16, 252, 18, 152, 64, 65, 128, 0, 76, 194, 46, 0, 4,
                    194, 4, 1, 68, 238, 80, 136, 40, 254, 64, 146, 16, 252, 18, 152, 64,
                    65, 128, 0, 52, 194, 46, 0, 8, 194, 4, 1, 72, 238, 80, 136, 40,
                    254, 64, 146, 16, 252, 18, 152, 64, 65, 128, 0, 28, 128, 163, 0, 0,
                    124, 4, 40, 0, 65, 130, 0, 16, 60, 192, 71, 195, 96, 198, 80, 0,
                    144, 196, 1, 72, 125, 239, 241, 32, 232, 131, 255, 200, 232, 163, 255, 192,
                    232, 195, 255, 184, 233, 195, 255, 216, 233, 227, 255, 208, 202, 3, 255, 224,
                    202, 35, 255, 232, 194, 67, 255, 180, 194, 99, 255, 176, 48, 100, 1, 64,
                    60, 128, 1, 10, 75, 14, 177, 64
                });
                enemyVoidTimer.Start();
            }
            else
            {
                // Restore normal hook
                api.setMemory(17665728U, new byte[]
                {
                    60, 96, 1, 144, 96, 99, 235, 176, 249, 195, 255, 216, 249, 227, 255, 208,
                    218, 3, 255, 224, 218, 35, 255, 232, 125, 224, 0, 38, 129, 195, 0, 20,
                    194, 4, 1, 64, 194, 46, 0, 0, 252, 16, 136, 64, 64, 130, 0, 40,
                    194, 4, 1, 68, 194, 46, 0, 4, 252, 16, 136, 64, 64, 130, 0, 24,
                    194, 4, 1, 72, 194, 46, 0, 8, 252, 16, 136, 64, 64, 130, 0, 8,
                    144, 131, 0, 0, 125, 239, 241, 32, 233, 195, 255, 216, 233, 227, 255, 208,
                    202, 3, 255, 224, 202, 35, 255, 232, 48, 100, 1, 64, 60, 128, 1, 10,
                    75, 14, 177, 228
                });
                api.setMemory(17665844U, new byte[164]);
                enemyVoidTimer.Stop();
            }
        }

        private void EnemyVoidTimer_Tick(object sender, EventArgs e)
        {
            // Timer keeps the enemies to void hook active
        }

        private void tglGameSpeed_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglGameSpeed.Checked)
                api.Extension.WriteFloat(811100260U, (float)sliderGameSpeed.Value);
            else
                api.Extension.WriteFloat(811100260U, 1f);
        }
        
        private void sliderGameSpeed_Scroll(object sender, EventArgs e)
        {
            UpdateGameSpeedValue();
        }

        private void tglInstantRevive_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            api.Extension.WriteUInt32(7455560U, tglInstantRevive.Checked ? 1610612736U : 2478768176U);
        }

        private void tglStopBullets_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            api.Extension.WriteFloat(5367776U, tglStopBullets.Checked ? 0f : 1f);
        }

        private void tglStaticActors_CheckedChanged(object sender, EventArgs e)
        {
            if (!isConnected) return;
            if (tglStaticActors.Checked)
            {
                api.Extension.WriteFloat(3070660U, 0f);
                return;
            }
            api.Extension.WriteFloat(3070660U, 1f);
        }
        
        // ═══════════════════════════════════════════════════════════════
        // QUICK ACTIONS
        // ═══════════════════════════════════════════════════════════════
        private void BtnEnableAllMods_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;
            tglGodMode.Checked = true;
            tglImmune.Checked = true;
            tglNoTarget.Checked = true;
            tglNoCollision.Checked = true;
            tglSuperSpeed.Checked = true;
            tglInfiniteAmmo.Checked = true;
            tglUnlimitedSparrow.Checked = true;
            tglUnlimitedAbilities.Checked = true;
            tglNoRecoil.Checked = true;
            tglOneHitKill.Checked = true;
            tglRapidFire.Checked = true;
            tglInstantRevive.Checked = true;
            tglStopBullets.Checked = true;
            tglStaticActors.Checked = true;
        }
        
        private void BtnDisableAllMods_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;
            tglGodMode.Checked = false;
            tglImmune.Checked = false;
            tglNoTarget.Checked = false;
            tglNoCollision.Checked = false;
            tglSuperSpeed.Checked = false;
            tglInfiniteAmmo.Checked = false;
            tglUnlimitedSparrow.Checked = false;
            tglUnlimitedAbilities.Checked = false;
            tglNoRecoil.Checked = false;
            tglNoSpread.Checked = false;
            tglOneHitKill.Checked = false;
            tglRapidFire.Checked = false;
            tglEnemiesVoid.Checked = false;
            tglInstantRevive.Checked = false;
            tglStopBullets.Checked = false;
            tglStaticActors.Checked = false;
            tglGameSpeed.Checked = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TAB BUTTON
    // ═══════════════════════════════════════════════════════════════
    public class TabButton : Control
    {
        public bool IsSelected { get; set; }
        private bool isHovered = false;

        public TabButton(string text, bool selected = false)
        {
            this.Text = text;
            this.IsSelected = selected;
            this.Size = new Size(135, 32);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color bgColor = IsSelected ? Form1.Theme.TabActive : (isHovered ? Form1.Theme.TabHover : Color.Transparent);
            Color textColor = IsSelected ? Form1.Theme.Text : Form1.Theme.TextDim;

            if (bgColor != Color.Transparent)
            {
                using (var brush = new SolidBrush(bgColor))
                    e.Graphics.FillRectangle(brush, 0, 0, this.Width, this.Height);
            }

            if (IsSelected)
            {
                using (var brush = new SolidBrush(Form1.Theme.Accent))
                    e.Graphics.FillRectangle(brush, 0, 0, 3, this.Height);
            }

            using (var font = new Font("Segoe UI", 9f))
            using (var brush = new SolidBrush(textColor))
            {
                var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(this.Text, font, brush, new RectangleF(15, 0, this.Width - 15, this.Height), sf);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TOGGLE SWITCH
    // ═══════════════════════════════════════════════════════════════
    public class ToggleSwitch : Control
    {
        private bool _checked = false;
        public bool Checked
        {
            get => _checked;
            set { _checked = value; CheckedChanged?.Invoke(this, EventArgs.Empty); Invalidate(); }
        }
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            this.Size = new Size(44, 22);
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color bgColor = this.Parent?.BackColor ?? Form1.Theme.Background;
            e.Graphics.Clear(bgColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            Color trackColor = _checked ? Form1.Theme.Accent : Color.FromArgb(55, 55, 55);
            using (var brush = new SolidBrush(trackColor))
                e.Graphics.FillRoundedRectangle(brush, rect, this.Height / 2);

            int thumbSize = this.Height - 6;
            int thumbX = _checked ? this.Width - thumbSize - 3 : 3;
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(brush, thumbX, 3, thumbSize, thumbSize);
        }
    }

    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DARK SCROLLBAR PANEL
    // ═══════════════════════════════════════════════════════════════
    public class DarkScrollPanel : Panel
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ShowScrollBar(IntPtr hWnd, int wBar, int bShow);
        
        private const int SB_HORZ = 0;
        private const int SB_VERT = 1;
        private const int SB_BOTH = 3;

        public DarkScrollPanel()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | 
                         ControlStyles.UserPaint | 
                         ControlStyles.OptimizedDoubleBuffer | 
                         ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ShowScrollBar(this.Handle, SB_BOTH, 0); // Hide both scrollbars
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Only draw custom scrollbar if content overflows
            if (this.AutoScroll && this.VerticalScroll.Visible)
            {
                const int scrollWidth = 10;
                
                // Draw scrollbar track background
                Rectangle trackRect = new Rectangle(
                    this.ClientSize.Width - scrollWidth,
                    0,
                    scrollWidth,
                    this.ClientSize.Height
                );

                using (var trackBrush = new SolidBrush(Form1.Theme.LeftPanel))
                {
                    e.Graphics.FillRectangle(trackBrush, trackRect);
                }

                // Calculate thumb dimensions
                int maxScroll = this.VerticalScroll.Maximum - this.ClientSize.Height + 1;
                if (maxScroll > 0)
                {
                    float visibleRatio = (float)this.ClientSize.Height / (float)this.VerticalScroll.Maximum;
                    int thumbHeight = Math.Max(40, (int)(this.ClientSize.Height * visibleRatio));
                    
                    float scrollPos = (float)this.VerticalScroll.Value / (float)maxScroll;
                    int thumbY = (int)(scrollPos * (this.ClientSize.Height - thumbHeight));

                    Rectangle thumbRect = new Rectangle(
                        this.ClientSize.Width - scrollWidth + 2,
                        thumbY + 2,
                        scrollWidth - 4,
                        thumbHeight - 4
                    );

                    // Draw thumb
                    using (var thumbBrush = new SolidBrush(Form1.Theme.TabActive))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        if (thumbRect.Height > 8)
                        {
                            using (var path = new GraphicsPath())
                            {
                                int radius = 3;
                                int d = radius * 2;
                                path.AddArc(thumbRect.X, thumbRect.Y, d, d, 180, 90);
                                path.AddArc(thumbRect.Right - d, thumbRect.Y, d, d, 270, 90);
                                path.AddArc(thumbRect.Right - d, thumbRect.Bottom - d, d, d, 0, 90);
                                path.AddArc(thumbRect.X, thumbRect.Bottom - d, d, d, 90, 90);
                                path.CloseFigure();
                                e.Graphics.FillPath(thumbBrush, path);
                            }
                        }
                        else
                        {
                            e.Graphics.FillRectangle(thumbBrush, thumbRect);
                        }
                    }
                }
            }
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            ShowScrollBar(this.Handle, SB_BOTH, 0); // Keep hidden
            this.Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            this.Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            const int WM_VSCROLL = 0x115;
            const int WM_MOUSEWHEEL = 0x20A;

            if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
            {
                // Keep native bars hidden without forcing extra paint passes.
                ShowScrollBar(this.Handle, SB_BOTH, 0);
            }
        }
    }
}
