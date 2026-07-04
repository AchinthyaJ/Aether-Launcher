namespace OfflineMinecraftLauncher
{
    partial class LauncherForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LauncherForm));
            headerPanel = new GradientPanel();
            heroMetaLabel = new Label();
            activeContextLabel = new Label();
            activeProfileBadge = new Label();
            subtitleLabel = new Label();
            titleLabel = new Label();
            contentLayout = new TableLayoutPanel();
            playCard = new Panel();
            playLayout = new TableLayoutPanel();
            playSectionLabel = new Label();
            usernameCaption = new Label();
            usernameInput = new TextBox();
            versionCaption = new Label();
            versionRow = new TableLayoutPanel();
            cbVersion = new ComboBox();
            minecraftVersion = new ComboBox();
            profileCaption = new Label();
            profileNameInput = new TextBox();
            profileActionRow = new TableLayoutPanel();
            profileLoaderCombo = new ComboBox();
            createProfileButton = new Button();
            installModeLabel = new Label();
            characterPanel = new Panel();
            characterHelpPictureBox = new PictureBox();
            characterPreviewCaption = new Label();
            characterPictureBox = new PictureBox();
            btnStart = new Button();
            statusLabel = new Label();
            installDetailsLabel = new Label();
            pbFiles = new ProgressBar();
            pbProgress = new ProgressBar();
            modrinthCard = new Panel();
            modrinthLayout = new TableLayoutPanel();
            modrinthSectionLabel = new Label();
            modrinthSearchRow = new TableLayoutPanel();
            modrinthSearchInput = new TextBox();
            modrinthProjectTypeCombo = new ComboBox();
            modrinthLoaderCombo = new ComboBox();
            modrinthSearchButton = new Button();
            modrinthFilterRow = new TableLayoutPanel();
            modrinthVersionLabel = new Label();
            modrinthVersionInput = new TextBox();
            modrinthResultsListView = new ListView();
            projectNameColumn = new ColumnHeader();
            projectTypeColumn = new ColumnHeader();
            projectDownloadsColumn = new ColumnHeader();
            projectAuthorColumn = new ColumnHeader();
            modrinthDetailsBox = new RichTextBox();
            modrinthActionRow = new TableLayoutPanel();
            installSelectedButton = new Button();
            importMrpackButton = new Button();
            profilesCaption = new Label();
            profileListBox = new ListBox();
            headerPanel.SuspendLayout();
            contentLayout.SuspendLayout();
            playCard.SuspendLayout();
            playLayout.SuspendLayout();
            versionRow.SuspendLayout();
            profileActionRow.SuspendLayout();
            characterPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)characterHelpPictureBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)characterPictureBox).BeginInit();
            modrinthCard.SuspendLayout();
            modrinthLayout.SuspendLayout();
            modrinthSearchRow.SuspendLayout();
            modrinthFilterRow.SuspendLayout();
            modrinthActionRow.SuspendLayout();
            SuspendLayout();
            // 
            // headerPanel
            // 
            headerPanel.Angle = 20F;
            headerPanel.Controls.Add(heroMetaLabel);
            headerPanel.Controls.Add(activeContextLabel);
            headerPanel.Controls.Add(activeProfileBadge);
            headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Dock = DockStyle.Top;
            headerPanel.EndColor = Color.FromArgb(9, 11, 22);
            headerPanel.Location = new Point(0, 0);
            headerPanel.Name = "headerPanel";
            headerPanel.Padding = new Padding(22, 18, 22, 18);
            headerPanel.Size = new Size(1424, 132);
            headerPanel.StartColor = Color.FromArgb(18, 29, 51);
            headerPanel.TabIndex = 0;
            // 
            // heroMetaLabel
            // 
            heroMetaLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            heroMetaLabel.AutoSize = true;
            heroMetaLabel.BackColor = Color.Transparent;
            heroMetaLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            heroMetaLabel.ForeColor = Color.FromArgb(138, 149, 173);
            heroMetaLabel.Location = new Point(1091, 23);
            heroMetaLabel.Name = "heroMetaLabel";
            heroMetaLabel.Size = new Size(290, 19);
            heroMetaLabel.TabIndex = 4;
            heroMetaLabel.Text = "Offline launch, isolated instances, Modrinth";
            // 
            // activeContextLabel
            // 
            activeContextLabel.AutoSize = true;
            activeContextLabel.BackColor = Color.Transparent;
            activeContextLabel.Font = new Font("Segoe UI", 11F);
            activeContextLabel.ForeColor = Color.FromArgb(176, 186, 207);
            activeContextLabel.Location = new Point(27, 92);
            activeContextLabel.Name = "activeContextLabel";
            activeContextLabel.Size = new Size(243, 20);
            activeContextLabel.TabIndex = 3;
            activeContextLabel.Text = "Launching default .minecraft quickly";
            // 
            // activeProfileBadge
            // 
            activeProfileBadge.AutoSize = true;
            activeProfileBadge.BackColor = Color.FromArgb(255, 120, 48);
            activeProfileBadge.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            activeProfileBadge.ForeColor = Color.White;
            activeProfileBadge.Location = new Point(29, 58);
            activeProfileBadge.Name = "activeProfileBadge";
            activeProfileBadge.Padding = new Padding(10, 5, 10, 5);
            activeProfileBadge.Size = new Size(101, 29);
            activeProfileBadge.TabIndex = 2;
            activeProfileBadge.Text = "Quick Launch";
            // 
            // subtitleLabel
            // 
            subtitleLabel.AutoSize = true;
            subtitleLabel.BackColor = Color.Transparent;
            subtitleLabel.Font = new Font("Segoe UI", 12F);
            subtitleLabel.ForeColor = Color.FromArgb(188, 199, 220);
            subtitleLabel.Location = new Point(27, 32);
            subtitleLabel.Name = "subtitleLabel";
            subtitleLabel.Size = new Size(467, 21);
            subtitleLabel.TabIndex = 1;
            subtitleLabel.Text = "A loud, fast launcher with profile-based mod installs and pack imports.";
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = new Font("Segoe UI Black", 22F, FontStyle.Bold);
            titleLabel.ForeColor = Color.White;
            titleLabel.Location = new Point(22, 12);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(244, 41);
            titleLabel.TabIndex = 0;
            titleLabel.Text = "Fugo Launcher";
            // 
            // contentLayout
            // 
            contentLayout.BackColor = Color.FromArgb(11, 13, 24);
            contentLayout.ColumnCount = 2;
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41F));
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59F));
            contentLayout.Controls.Add(playCard, 0, 0);
            contentLayout.Controls.Add(modrinthCard, 1, 0);
            contentLayout.Dock = DockStyle.Fill;
            contentLayout.Location = new Point(0, 132);
            contentLayout.Name = "contentLayout";
            contentLayout.Padding = new Padding(18);
            contentLayout.RowCount = 1;
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            contentLayout.Size = new Size(1424, 728);
            contentLayout.TabIndex = 1;
            // 
            // playCard
            // 
            playCard.BackColor = Color.FromArgb(18, 22, 35);
            playCard.Controls.Add(playLayout);
            playCard.Dock = DockStyle.Fill;
            playCard.Location = new Point(21, 21);
            playCard.Name = "playCard";
            playCard.Padding = new Padding(18);
            playCard.Size = new Size(564, 686);
            playCard.TabIndex = 0;
            // 
            // playLayout
            // 
            playLayout.ColumnCount = 1;
            playLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            playLayout.Controls.Add(playSectionLabel, 0, 0);
            playLayout.Controls.Add(usernameCaption, 0, 1);
            playLayout.Controls.Add(usernameInput, 0, 2);
            playLayout.Controls.Add(versionCaption, 0, 3);
            playLayout.Controls.Add(versionRow, 0, 4);
            playLayout.Controls.Add(profileCaption, 0, 5);
            playLayout.Controls.Add(profileNameInput, 0, 6);
            playLayout.Controls.Add(profileActionRow, 0, 7);
            playLayout.Controls.Add(installModeLabel, 0, 8);
            playLayout.Controls.Add(characterPanel, 0, 9);
            playLayout.Controls.Add(btnStart, 0, 10);
            playLayout.Controls.Add(statusLabel, 0, 11);
            playLayout.Controls.Add(installDetailsLabel, 0, 12);
            playLayout.Controls.Add(pbFiles, 0, 13);
            playLayout.Controls.Add(pbProgress, 0, 14);
            playLayout.Dock = DockStyle.Fill;
            playLayout.Location = new Point(18, 18);
            playLayout.Name = "playLayout";
            playLayout.RowCount = 15;
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            playLayout.Size = new Size(528, 650);
            playLayout.TabIndex = 0;
            // 
            // playSectionLabel
            // 
            playSectionLabel.AutoSize = true;
            playSectionLabel.Dock = DockStyle.Fill;
            playSectionLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
            playSectionLabel.ForeColor = Color.White;
            playSectionLabel.Location = new Point(3, 0);
            playSectionLabel.Name = "playSectionLabel";
            playSectionLabel.Size = new Size(522, 36);
            playSectionLabel.TabIndex = 0;
            playSectionLabel.Text = "Launch Deck";
            playSectionLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // usernameCaption
            // 
            usernameCaption.AutoSize = true;
            usernameCaption.Dock = DockStyle.Fill;
            usernameCaption.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            usernameCaption.ForeColor = Color.FromArgb(185, 193, 211);
            usernameCaption.Location = new Point(3, 36);
            usernameCaption.Name = "usernameCaption";
            usernameCaption.Size = new Size(522, 24);
            usernameCaption.TabIndex = 1;
            usernameCaption.Text = "Username";
            usernameCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // usernameInput
            // 
            usernameInput.BackColor = Color.FromArgb(25, 31, 49);
            usernameInput.BorderStyle = BorderStyle.FixedSingle;
            usernameInput.Dock = DockStyle.Fill;
            usernameInput.Font = new Font("Segoe UI", 12F);
            usernameInput.ForeColor = Color.White;
            usernameInput.Location = new Point(3, 63);
            usernameInput.Name = "usernameInput";
            usernameInput.Size = new Size(522, 29);
            usernameInput.TabIndex = 2;
            usernameInput.TextChanged += usernameInput_TextChanged;
            // 
            // versionCaption
            // 
            versionCaption.AutoSize = true;
            versionCaption.Dock = DockStyle.Fill;
            versionCaption.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            versionCaption.ForeColor = Color.FromArgb(185, 193, 211);
            versionCaption.Location = new Point(3, 102);
            versionCaption.Name = "versionCaption";
            versionCaption.Size = new Size(522, 24);
            versionCaption.TabIndex = 3;
            versionCaption.Text = "Minecraft Version";
            versionCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // versionRow
            // 
            versionRow.ColumnCount = 2;
            versionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            versionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            versionRow.Controls.Add(cbVersion, 0, 0);
            versionRow.Controls.Add(minecraftVersion, 1, 0);
            versionRow.Dock = DockStyle.Fill;
            versionRow.Location = new Point(0, 126);
            versionRow.Margin = new Padding(0);
            versionRow.Name = "versionRow";
            versionRow.RowCount = 1;
            versionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            versionRow.Size = new Size(528, 44);
            versionRow.TabIndex = 4;
            // 
            // cbVersion
            // 
            cbVersion.BackColor = Color.FromArgb(25, 31, 49);
            cbVersion.Dock = DockStyle.Fill;
            cbVersion.FlatStyle = FlatStyle.Flat;
            cbVersion.Font = new Font("Segoe UI", 11F);
            cbVersion.ForeColor = Color.White;
            cbVersion.FormattingEnabled = true;
            cbVersion.Location = new Point(3, 7);
            cbVersion.Margin = new Padding(3, 7, 6, 7);
            cbVersion.Name = "cbVersion";
            cbVersion.Size = new Size(307, 28);
            cbVersion.TabIndex = 0;
            cbVersion.TextChanged += cbVersion_TextChanged;
            // 
            // minecraftVersion
            // 
            minecraftVersion.BackColor = Color.FromArgb(25, 31, 49);
            minecraftVersion.Dock = DockStyle.Fill;
            minecraftVersion.FlatStyle = FlatStyle.Flat;
            minecraftVersion.Font = new Font("Segoe UI", 10F);
            minecraftVersion.ForeColor = Color.White;
            minecraftVersion.FormattingEnabled = true;
            minecraftVersion.Items.AddRange(new object[] { "Releases and Installed", "All Versions" });
            minecraftVersion.Location = new Point(319, 7);
            minecraftVersion.Margin = new Padding(3, 7, 3, 7);
            minecraftVersion.Name = "minecraftVersion";
            minecraftVersion.Size = new Size(206, 25);
            minecraftVersion.TabIndex = 1;
            minecraftVersion.SelectedIndexChanged += minecraftVersion_SelectedIndexChanged;
            // 
            // profileCaption
            // 
            profileCaption.AutoSize = true;
            profileCaption.Dock = DockStyle.Fill;
            profileCaption.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            profileCaption.ForeColor = Color.FromArgb(185, 193, 211);
            profileCaption.Location = new Point(3, 170);
            profileCaption.Name = "profileCaption";
            profileCaption.Size = new Size(522, 24);
            profileCaption.TabIndex = 5;
            profileCaption.Text = "New Profile";
            profileCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // profileNameInput
            // 
            profileNameInput.BackColor = Color.FromArgb(25, 31, 49);
            profileNameInput.BorderStyle = BorderStyle.FixedSingle;
            profileNameInput.Dock = DockStyle.Fill;
            profileNameInput.Font = new Font("Segoe UI", 12F);
            profileNameInput.ForeColor = Color.White;
            profileNameInput.Location = new Point(3, 197);
            profileNameInput.Name = "profileNameInput";
            profileNameInput.PlaceholderText = "Speedrun Fabric, PvP Kit, Vanilla Chill...";
            profileNameInput.Size = new Size(522, 29);
            profileNameInput.TabIndex = 6;
            // 
            // profileActionRow
            // 
            profileActionRow.ColumnCount = 2;
            profileActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            profileActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            profileActionRow.Controls.Add(profileLoaderCombo, 0, 0);
            profileActionRow.Controls.Add(createProfileButton, 1, 0);
            profileActionRow.Dock = DockStyle.Fill;
            profileActionRow.Location = new Point(0, 236);
            profileActionRow.Margin = new Padding(0);
            profileActionRow.Name = "profileActionRow";
            profileActionRow.RowCount = 1;
            profileActionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            profileActionRow.Size = new Size(528, 44);
            profileActionRow.TabIndex = 7;
            // 
            // profileLoaderCombo
            // 
            profileLoaderCombo.BackColor = Color.FromArgb(25, 31, 49);
            profileLoaderCombo.Dock = DockStyle.Fill;
            profileLoaderCombo.FlatStyle = FlatStyle.Flat;
            profileLoaderCombo.Font = new Font("Segoe UI", 10.5F);
            profileLoaderCombo.ForeColor = Color.White;
            profileLoaderCombo.FormattingEnabled = true;
            profileLoaderCombo.Items.AddRange(new object[] { "Vanilla", "Fabric" });
            profileLoaderCombo.Location = new Point(3, 7);
            profileLoaderCombo.Margin = new Padding(3, 7, 6, 7);
            profileLoaderCombo.Name = "profileLoaderCombo";
            profileLoaderCombo.Size = new Size(191, 27);
            profileLoaderCombo.TabIndex = 0;
            // 
            // createProfileButton
            // 
            createProfileButton.BackColor = Color.FromArgb(62, 214, 180);
            createProfileButton.Dock = DockStyle.Fill;
            createProfileButton.FlatAppearance.BorderSize = 0;
            createProfileButton.FlatStyle = FlatStyle.Flat;
            createProfileButton.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            createProfileButton.ForeColor = Color.FromArgb(7, 19, 23);
            createProfileButton.Location = new Point(203, 6);
            createProfileButton.Margin = new Padding(3, 6, 3, 6);
            createProfileButton.Name = "createProfileButton";
            createProfileButton.Size = new Size(322, 32);
            createProfileButton.TabIndex = 1;
            createProfileButton.Text = "Create Isolated Profile";
            createProfileButton.UseVisualStyleBackColor = false;
            createProfileButton.Click += createProfileButton_Click;
            // 
            // installModeLabel
            // 
            installModeLabel.Dock = DockStyle.Fill;
            installModeLabel.Font = new Font("Segoe UI", 10F);
            installModeLabel.ForeColor = Color.FromArgb(133, 143, 165);
            installModeLabel.Location = new Point(3, 280);
            installModeLabel.Name = "installModeLabel";
            installModeLabel.Size = new Size(522, 42);
            installModeLabel.TabIndex = 8;
            installModeLabel.Text = "Select a profile below to install mods into an isolated instance.";
            installModeLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // characterPanel
            // 
            characterPanel.BackColor = Color.FromArgb(13, 17, 28);
            characterPanel.Controls.Add(characterHelpPictureBox);
            characterPanel.Controls.Add(characterPreviewCaption);
            characterPanel.Controls.Add(characterPictureBox);
            characterPanel.Dock = DockStyle.Fill;
            characterPanel.Location = new Point(3, 325);
            characterPanel.Name = "characterPanel";
            characterPanel.Padding = new Padding(18);
            characterPanel.Size = new Size(522, 175);
            characterPanel.TabIndex = 9;
            // 
            // characterHelpPictureBox
            // 
            characterHelpPictureBox.Image = Properties.Resources.icon_info;
            characterHelpPictureBox.Location = new Point(131, 19);
            characterHelpPictureBox.Name = "characterHelpPictureBox";
            characterHelpPictureBox.Size = new Size(16, 16);
            characterHelpPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            characterHelpPictureBox.TabIndex = 2;
            characterHelpPictureBox.TabStop = false;
            characterHelpPictureBox.Tag = "Offline skins are derived from the username and chosen Minecraft version.";
            // 
            // characterPreviewCaption
            // 
            characterPreviewCaption.AutoSize = true;
            characterPreviewCaption.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            characterPreviewCaption.ForeColor = Color.White;
            characterPreviewCaption.Location = new Point(18, 17);
            characterPreviewCaption.Name = "characterPreviewCaption";
            characterPreviewCaption.Size = new Size(98, 19);
            characterPreviewCaption.TabIndex = 1;
            characterPreviewCaption.Text = "Skin Preview";
            // 
            // characterPictureBox
            // 
            characterPictureBox.Anchor = AnchorStyles.None;
            characterPictureBox.BackColor = Color.Transparent;
            characterPictureBox.Image = Properties.Resources.Steve_classic;
            characterPictureBox.Location = new Point(172, 37);
            characterPictureBox.Name = "characterPictureBox";
            characterPictureBox.Size = new Size(178, 122);
            characterPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            characterPictureBox.TabIndex = 0;
            characterPictureBox.TabStop = false;
            characterPictureBox.Tag = "Steve classic";
            // 
            // btnStart
            // 
            btnStart.BackColor = Color.FromArgb(255, 120, 48);
            btnStart.Dock = DockStyle.Fill;
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.Font = new Font("Segoe UI Black", 13F, FontStyle.Bold);
            btnStart.ForeColor = Color.White;
            btnStart.Location = new Point(3, 506);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(522, 48);
            btnStart.TabIndex = 10;
            btnStart.Text = "Launch";
            btnStart.UseVisualStyleBackColor = false;
            btnStart.Click += btnStart_Click;
            // 
            // statusLabel
            // 
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            statusLabel.ForeColor = Color.White;
            statusLabel.Location = new Point(3, 557);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(522, 28);
            statusLabel.TabIndex = 11;
            statusLabel.Text = "Ready";
            statusLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // installDetailsLabel
            // 
            installDetailsLabel.AutoEllipsis = true;
            installDetailsLabel.Dock = DockStyle.Fill;
            installDetailsLabel.Font = new Font("Segoe UI", 9F);
            installDetailsLabel.ForeColor = Color.FromArgb(142, 152, 172);
            installDetailsLabel.Location = new Point(3, 585);
            installDetailsLabel.Name = "installDetailsLabel";
            installDetailsLabel.Size = new Size(522, 22);
            installDetailsLabel.TabIndex = 12;
            installDetailsLabel.Text = "No active install.";
            installDetailsLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // pbFiles
            // 
            pbFiles.Dock = DockStyle.Fill;
            pbFiles.Location = new Point(3, 610);
            pbFiles.Maximum = 100;
            pbFiles.Name = "pbFiles";
            pbFiles.Size = new Size(522, 16);
            pbFiles.Style = ProgressBarStyle.Continuous;
            pbFiles.TabIndex = 13;
            // 
            // pbProgress
            // 
            pbProgress.Dock = DockStyle.Fill;
            pbProgress.Location = new Point(3, 632);
            pbProgress.Maximum = 100;
            pbProgress.Name = "pbProgress";
            pbProgress.Size = new Size(522, 15);
            pbProgress.Style = ProgressBarStyle.Continuous;
            pbProgress.TabIndex = 14;
            // 
            // modrinthCard
            // 
            modrinthCard.BackColor = Color.FromArgb(18, 22, 35);
            modrinthCard.Controls.Add(modrinthLayout);
            modrinthCard.Dock = DockStyle.Fill;
            modrinthCard.Location = new Point(591, 21);
            modrinthCard.Name = "modrinthCard";
            modrinthCard.Padding = new Padding(18);
            modrinthCard.Size = new Size(812, 686);
            modrinthCard.TabIndex = 1;
            // 
            // modrinthLayout
            // 
            modrinthLayout.ColumnCount = 1;
            modrinthLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modrinthLayout.Controls.Add(modrinthSectionLabel, 0, 0);
            modrinthLayout.Controls.Add(modrinthSearchRow, 0, 1);
            modrinthLayout.Controls.Add(modrinthFilterRow, 0, 2);
            modrinthLayout.Controls.Add(modrinthResultsListView, 0, 3);
            modrinthLayout.Controls.Add(modrinthDetailsBox, 0, 4);
            modrinthLayout.Controls.Add(modrinthActionRow, 0, 5);
            modrinthLayout.Controls.Add(profilesCaption, 0, 6);
            modrinthLayout.Controls.Add(profileListBox, 0, 7);
            modrinthLayout.Dock = DockStyle.Fill;
            modrinthLayout.Location = new Point(18, 18);
            modrinthLayout.Name = "modrinthLayout";
            modrinthLayout.RowCount = 8;
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            modrinthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            modrinthLayout.Size = new Size(776, 650);
            modrinthLayout.TabIndex = 0;
            // 
            // modrinthSectionLabel
            // 
            modrinthSectionLabel.AutoSize = true;
            modrinthSectionLabel.Dock = DockStyle.Fill;
            modrinthSectionLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
            modrinthSectionLabel.ForeColor = Color.White;
            modrinthSectionLabel.Location = new Point(3, 0);
            modrinthSectionLabel.Name = "modrinthSectionLabel";
            modrinthSectionLabel.Size = new Size(770, 36);
            modrinthSectionLabel.TabIndex = 0;
            modrinthSectionLabel.Text = "Modrinth Arsenal";
            modrinthSectionLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // modrinthSearchRow
            // 
            modrinthSearchRow.ColumnCount = 4;
            modrinthSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            modrinthSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17F));
            modrinthSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16F));
            modrinthSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            modrinthSearchRow.Controls.Add(modrinthSearchInput, 0, 0);
            modrinthSearchRow.Controls.Add(modrinthProjectTypeCombo, 1, 0);
            modrinthSearchRow.Controls.Add(modrinthLoaderCombo, 2, 0);
            modrinthSearchRow.Controls.Add(modrinthSearchButton, 3, 0);
            modrinthSearchRow.Dock = DockStyle.Fill;
            modrinthSearchRow.Location = new Point(0, 36);
            modrinthSearchRow.Margin = new Padding(0);
            modrinthSearchRow.Name = "modrinthSearchRow";
            modrinthSearchRow.RowCount = 1;
            modrinthSearchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            modrinthSearchRow.Size = new Size(776, 46);
            modrinthSearchRow.TabIndex = 1;
            // 
            // modrinthSearchInput
            // 
            modrinthSearchInput.BackColor = Color.FromArgb(25, 31, 49);
            modrinthSearchInput.BorderStyle = BorderStyle.FixedSingle;
            modrinthSearchInput.Dock = DockStyle.Fill;
            modrinthSearchInput.Font = new Font("Segoe UI", 11F);
            modrinthSearchInput.ForeColor = Color.White;
            modrinthSearchInput.Location = new Point(3, 8);
            modrinthSearchInput.Margin = new Padding(3, 8, 6, 8);
            modrinthSearchInput.Name = "modrinthSearchInput";
            modrinthSearchInput.PlaceholderText = "Search for shaders, optimization mods, adventure packs...";
            modrinthSearchInput.Size = new Size(340, 27);
            modrinthSearchInput.TabIndex = 0;
            // 
            // modrinthProjectTypeCombo
            // 
            modrinthProjectTypeCombo.BackColor = Color.FromArgb(25, 31, 49);
            modrinthProjectTypeCombo.Dock = DockStyle.Fill;
            modrinthProjectTypeCombo.FlatStyle = FlatStyle.Flat;
            modrinthProjectTypeCombo.Font = new Font("Segoe UI", 10.5F);
            modrinthProjectTypeCombo.ForeColor = Color.White;
            modrinthProjectTypeCombo.FormattingEnabled = true;
            modrinthProjectTypeCombo.Items.AddRange(new object[] { "Mod", "Modpack" });
            modrinthProjectTypeCombo.Location = new Point(352, 8);
            modrinthProjectTypeCombo.Margin = new Padding(3, 8, 6, 8);
            modrinthProjectTypeCombo.Name = "modrinthProjectTypeCombo";
            modrinthProjectTypeCombo.Size = new Size(122, 27);
            modrinthProjectTypeCombo.TabIndex = 1;
            // 
            // modrinthLoaderCombo
            // 
            modrinthLoaderCombo.BackColor = Color.FromArgb(25, 31, 49);
            modrinthLoaderCombo.Dock = DockStyle.Fill;
            modrinthLoaderCombo.FlatStyle = FlatStyle.Flat;
            modrinthLoaderCombo.Font = new Font("Segoe UI", 10F);
            modrinthLoaderCombo.ForeColor = Color.White;
            modrinthLoaderCombo.FormattingEnabled = true;
            modrinthLoaderCombo.Items.AddRange(new object[] { "Any", "Vanilla", "Fabric", "Quilt", "Forge", "NeoForge" });
            modrinthLoaderCombo.Location = new Point(483, 8);
            modrinthLoaderCombo.Margin = new Padding(3, 8, 6, 8);
            modrinthLoaderCombo.Name = "modrinthLoaderCombo";
            modrinthLoaderCombo.Size = new Size(118, 25);
            modrinthLoaderCombo.TabIndex = 2;
            // 
            // modrinthSearchButton
            // 
            modrinthSearchButton.BackColor = Color.FromArgb(102, 87, 255);
            modrinthSearchButton.Dock = DockStyle.Fill;
            modrinthSearchButton.FlatAppearance.BorderSize = 0;
            modrinthSearchButton.FlatStyle = FlatStyle.Flat;
            modrinthSearchButton.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            modrinthSearchButton.ForeColor = Color.White;
            modrinthSearchButton.Location = new Point(610, 6);
            modrinthSearchButton.Margin = new Padding(3, 6, 3, 6);
            modrinthSearchButton.Name = "modrinthSearchButton";
            modrinthSearchButton.Size = new Size(163, 34);
            modrinthSearchButton.TabIndex = 3;
            modrinthSearchButton.Text = "Search Modrinth";
            modrinthSearchButton.UseVisualStyleBackColor = false;
            modrinthSearchButton.Click += modrinthSearchButton_Click;
            // 
            // modrinthFilterRow
            // 
            modrinthFilterRow.ColumnCount = 2;
            modrinthFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
            modrinthFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modrinthFilterRow.Controls.Add(modrinthVersionLabel, 0, 0);
            modrinthFilterRow.Controls.Add(modrinthVersionInput, 1, 0);
            modrinthFilterRow.Dock = DockStyle.Fill;
            modrinthFilterRow.Location = new Point(0, 82);
            modrinthFilterRow.Margin = new Padding(0);
            modrinthFilterRow.Name = "modrinthFilterRow";
            modrinthFilterRow.RowCount = 1;
            modrinthFilterRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            modrinthFilterRow.Size = new Size(776, 36);
            modrinthFilterRow.TabIndex = 2;
            // 
            // modrinthVersionLabel
            // 
            modrinthVersionLabel.AutoSize = true;
            modrinthVersionLabel.Dock = DockStyle.Fill;
            modrinthVersionLabel.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            modrinthVersionLabel.ForeColor = Color.FromArgb(185, 193, 211);
            modrinthVersionLabel.Location = new Point(3, 0);
            modrinthVersionLabel.Name = "modrinthVersionLabel";
            modrinthVersionLabel.Size = new Size(122, 36);
            modrinthVersionLabel.TabIndex = 0;
            modrinthVersionLabel.Text = "Game Version";
            modrinthVersionLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // modrinthVersionInput
            // 
            modrinthVersionInput.BackColor = Color.FromArgb(25, 31, 49);
            modrinthVersionInput.BorderStyle = BorderStyle.FixedSingle;
            modrinthVersionInput.Dock = DockStyle.Fill;
            modrinthVersionInput.Font = new Font("Segoe UI", 10.5F);
            modrinthVersionInput.ForeColor = Color.White;
            modrinthVersionInput.Location = new Point(131, 5);
            modrinthVersionInput.Margin = new Padding(3, 5, 3, 5);
            modrinthVersionInput.Name = "modrinthVersionInput";
            modrinthVersionInput.Size = new Size(642, 26);
            modrinthVersionInput.TabIndex = 1;
            // 
            // modrinthResultsListView
            // 
            modrinthResultsListView.BackColor = Color.FromArgb(13, 17, 28);
            modrinthResultsListView.BorderStyle = BorderStyle.None;
            modrinthResultsListView.Columns.AddRange(new ColumnHeader[] { projectNameColumn, projectTypeColumn, projectDownloadsColumn, projectAuthorColumn });
            modrinthResultsListView.Dock = DockStyle.Fill;
            modrinthResultsListView.ForeColor = Color.White;
            modrinthResultsListView.FullRowSelect = true;
            modrinthResultsListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            modrinthResultsListView.Location = new Point(3, 121);
            modrinthResultsListView.MultiSelect = false;
            modrinthResultsListView.Name = "modrinthResultsListView";
            modrinthResultsListView.Size = new Size(770, 198);
            modrinthResultsListView.TabIndex = 3;
            modrinthResultsListView.UseCompatibleStateImageBehavior = false;
            modrinthResultsListView.View = View.Details;
            modrinthResultsListView.SelectedIndexChanged += modrinthResultsListView_SelectedIndexChanged;
            // 
            // projectNameColumn
            // 
            projectNameColumn.Text = "Project";
            projectNameColumn.Width = 275;
            // 
            // projectTypeColumn
            // 
            projectTypeColumn.Text = "Type";
            projectTypeColumn.Width = 90;
            // 
            // projectDownloadsColumn
            // 
            projectDownloadsColumn.Text = "Downloads";
            projectDownloadsColumn.Width = 110;
            // 
            // projectAuthorColumn
            // 
            projectAuthorColumn.Text = "Author";
            projectAuthorColumn.Width = 140;
            // 
            // modrinthDetailsBox
            // 
            modrinthDetailsBox.BackColor = Color.FromArgb(13, 17, 28);
            modrinthDetailsBox.BorderStyle = BorderStyle.None;
            modrinthDetailsBox.Dock = DockStyle.Fill;
            modrinthDetailsBox.Font = new Font("Segoe UI", 10F);
            modrinthDetailsBox.ForeColor = Color.FromArgb(211, 217, 230);
            modrinthDetailsBox.Location = new Point(3, 325);
            modrinthDetailsBox.Name = "modrinthDetailsBox";
            modrinthDetailsBox.ReadOnly = true;
            modrinthDetailsBox.Size = new Size(770, 143);
            modrinthDetailsBox.TabIndex = 4;
            modrinthDetailsBox.Text = "Search Modrinth to browse mods and modpacks.";
            // 
            // modrinthActionRow
            // 
            modrinthActionRow.ColumnCount = 2;
            modrinthActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            modrinthActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            modrinthActionRow.Controls.Add(installSelectedButton, 0, 0);
            modrinthActionRow.Controls.Add(importMrpackButton, 1, 0);
            modrinthActionRow.Dock = DockStyle.Fill;
            modrinthActionRow.Location = new Point(0, 471);
            modrinthActionRow.Margin = new Padding(0);
            modrinthActionRow.Name = "modrinthActionRow";
            modrinthActionRow.RowCount = 1;
            modrinthActionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            modrinthActionRow.Size = new Size(776, 48);
            modrinthActionRow.TabIndex = 5;
            // 
            // installSelectedButton
            // 
            installSelectedButton.BackColor = Color.FromArgb(62, 214, 180);
            installSelectedButton.Dock = DockStyle.Fill;
            installSelectedButton.Enabled = false;
            installSelectedButton.FlatAppearance.BorderSize = 0;
            installSelectedButton.FlatStyle = FlatStyle.Flat;
            installSelectedButton.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            installSelectedButton.ForeColor = Color.FromArgb(7, 19, 23);
            installSelectedButton.Location = new Point(3, 6);
            installSelectedButton.Margin = new Padding(3, 6, 6, 6);
            installSelectedButton.Name = "installSelectedButton";
            installSelectedButton.Size = new Size(495, 36);
            installSelectedButton.TabIndex = 0;
            installSelectedButton.Text = "Install Selected";
            installSelectedButton.UseVisualStyleBackColor = false;
            installSelectedButton.Click += installSelectedButton_Click;
            // 
            // importMrpackButton
            // 
            importMrpackButton.BackColor = Color.FromArgb(34, 39, 61);
            importMrpackButton.Dock = DockStyle.Fill;
            importMrpackButton.FlatAppearance.BorderColor = Color.FromArgb(78, 91, 126);
            importMrpackButton.FlatStyle = FlatStyle.Flat;
            importMrpackButton.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            importMrpackButton.ForeColor = Color.White;
            importMrpackButton.Location = new Point(510, 6);
            importMrpackButton.Margin = new Padding(6);
            importMrpackButton.Name = "importMrpackButton";
            importMrpackButton.Size = new Size(260, 36);
            importMrpackButton.TabIndex = 1;
            importMrpackButton.Text = "Import Local .mrpack";
            importMrpackButton.UseVisualStyleBackColor = false;
            importMrpackButton.Click += importMrpackButton_Click;
            // 
            // profilesCaption
            // 
            profilesCaption.AutoSize = true;
            profilesCaption.Dock = DockStyle.Fill;
            profilesCaption.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            profilesCaption.ForeColor = Color.White;
            profilesCaption.Location = new Point(3, 519);
            profilesCaption.Name = "profilesCaption";
            profilesCaption.Size = new Size(770, 26);
            profilesCaption.TabIndex = 6;
            profilesCaption.Text = "Installed Profiles (double-click to quick launch)";
            profilesCaption.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // profileListBox
            // 
            profileListBox.BackColor = Color.FromArgb(13, 17, 28);
            profileListBox.BorderStyle = BorderStyle.None;
            profileListBox.Dock = DockStyle.Fill;
            profileListBox.Font = new Font("Segoe UI", 10.5F);
            profileListBox.ForeColor = Color.White;
            profileListBox.FormattingEnabled = true;
            profileListBox.ItemHeight = 19;
            profileListBox.Location = new Point(3, 548);
            profileListBox.Name = "profileListBox";
            profileListBox.Size = new Size(770, 99);
            profileListBox.TabIndex = 7;
            profileListBox.DoubleClick += profileListBox_DoubleClick;
            profileListBox.SelectedIndexChanged += profileListBox_SelectedIndexChanged;
            // 
            // LauncherForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(11, 13, 24);
            ClientSize = new Size(1424, 860);
            Controls.Add(contentLayout);
            Controls.Add(headerPanel);
            Font = new Font("Segoe UI", 9F);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(1260, 780);
            Name = "LauncherForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Fugo Launcher";
            Load += LauncherForm_Load;
            headerPanel.ResumeLayout(false);
            headerPanel.PerformLayout();
            contentLayout.ResumeLayout(false);
            playCard.ResumeLayout(false);
            playLayout.ResumeLayout(false);
            playLayout.PerformLayout();
            versionRow.ResumeLayout(false);
            profileActionRow.ResumeLayout(false);
            characterPanel.ResumeLayout(false);
            characterPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)characterHelpPictureBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)characterPictureBox).EndInit();
            modrinthCard.ResumeLayout(false);
            modrinthLayout.ResumeLayout(false);
            modrinthLayout.PerformLayout();
            modrinthSearchRow.ResumeLayout(false);
            modrinthSearchRow.PerformLayout();
            modrinthFilterRow.ResumeLayout(false);
            modrinthFilterRow.PerformLayout();
            modrinthActionRow.ResumeLayout(false);
            ResumeLayout(false);
        }

        private GradientPanel headerPanel;
        private Label heroMetaLabel;
        private Label activeContextLabel;
        private Label activeProfileBadge;
        private Label subtitleLabel;
        private Label titleLabel;
        private TableLayoutPanel contentLayout;
        private Panel playCard;
        private TableLayoutPanel playLayout;
        private Label playSectionLabel;
        private Label usernameCaption;
        private TextBox usernameInput;
        private Label versionCaption;
        private TableLayoutPanel versionRow;
        private ComboBox cbVersion;
        private ComboBox minecraftVersion;
        private Label profileCaption;
        private TextBox profileNameInput;
        private TableLayoutPanel profileActionRow;
        private ComboBox profileLoaderCombo;
        private Button createProfileButton;
        private Label installModeLabel;
        private Panel characterPanel;
        private PictureBox characterHelpPictureBox;
        private Label characterPreviewCaption;
        private PictureBox characterPictureBox;
        private Button btnStart;
        private Label statusLabel;
        private Label installDetailsLabel;
        private ProgressBar pbFiles;
        private ProgressBar pbProgress;
        private Panel modrinthCard;
        private TableLayoutPanel modrinthLayout;
        private Label modrinthSectionLabel;
        private TableLayoutPanel modrinthSearchRow;
        private TextBox modrinthSearchInput;
        private ComboBox modrinthProjectTypeCombo;
        private ComboBox modrinthLoaderCombo;
        private Button modrinthSearchButton;
        private TableLayoutPanel modrinthFilterRow;
        private Label modrinthVersionLabel;
        private TextBox modrinthVersionInput;
        private ListView modrinthResultsListView;
        private ColumnHeader projectNameColumn;
        private ColumnHeader projectTypeColumn;
        private ColumnHeader projectDownloadsColumn;
        private ColumnHeader projectAuthorColumn;
        private RichTextBox modrinthDetailsBox;
        private TableLayoutPanel modrinthActionRow;
        private Button installSelectedButton;
        private Button importMrpackButton;
        private Label profilesCaption;
        private ListBox profileListBox;
    }
}
