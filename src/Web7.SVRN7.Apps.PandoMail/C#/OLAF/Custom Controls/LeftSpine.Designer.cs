namespace Web7.SVRN7.Apps
{
	partial class LeftSpine
	{
		/// <summary> 
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LeftSpine));
            this.stackStripSplitter = new System.Windows.Forms.SplitContainer();
            this.folderView1 = new Web7.SVRN7.Apps.FolderView();
            this.headerStrip1 = new Web7.SVRN7.Apps.HeaderStrip();
            this.toolStripLabel2 = new System.Windows.Forms.ToolStripLabel();
            this.headerStrip2 = new Web7.SVRN7.Apps.HeaderStrip();
            this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            this.stackStrip = new Web7.SVRN7.Apps.StackStrip();
            this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton2 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton3 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton4 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton5 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton6 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton7 = new System.Windows.Forms.ToolStripButton();
            this.overflowStrip = new Web7.SVRN7.Apps.BaseStackStrip();
            this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
            this.addorRemoveButtonsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.stackStripSplitter.Panel1.SuspendLayout();
            this.stackStripSplitter.Panel2.SuspendLayout();
            this.stackStripSplitter.SuspendLayout();
            this.headerStrip1.SuspendLayout();
            this.headerStrip2.SuspendLayout();
            this.stackStrip.SuspendLayout();
            this.overflowStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // stackStripSplitter
            // 
            this.stackStripSplitter.BackColor = System.Drawing.Color.Transparent;
            this.stackStripSplitter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stackStripSplitter.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.stackStripSplitter.Location = new System.Drawing.Point(0, 0);
            this.stackStripSplitter.Name = "stackStripSplitter";
            this.stackStripSplitter.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // stackStripSplitter.Panel1
            // 
            this.stackStripSplitter.Panel1.Controls.Add(this.folderView1);
            this.stackStripSplitter.Panel1.Controls.Add(this.headerStrip1);
            this.stackStripSplitter.Panel1.Controls.Add(this.headerStrip2);
            // 
            // stackStripSplitter.Panel2
            // 
            this.stackStripSplitter.Panel2.Controls.Add(this.stackStrip);
            this.stackStripSplitter.Panel2.Controls.Add(this.overflowStrip);
            this.stackStripSplitter.Size = new System.Drawing.Size(312, 629);
            this.stackStripSplitter.SplitterDistance = 330;
            this.stackStripSplitter.SplitterWidth = 7;
            this.stackStripSplitter.TabIndex = 0;
            this.stackStripSplitter.TabStop = false;
            this.stackStripSplitter.Text = "splitContainer1";
            this.stackStripSplitter.SplitterMoved += new System.Windows.Forms.SplitterEventHandler(this.stackStripSplitter_SplitterMoved);
            this.stackStripSplitter.Paint += new System.Windows.Forms.PaintEventHandler(this.splitContainer1_Paint);
            // 
            // folderView1
            // 
            this.folderView1.BackColor = System.Drawing.SystemColors.Window;
            this.folderView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.folderView1.Font = new System.Drawing.Font("Tahoma", 11F, System.Drawing.FontStyle.Bold);
            this.folderView1.Location = new System.Drawing.Point(0, 44);
            this.folderView1.Name = "folderView1";
            this.folderView1.Padding = new System.Windows.Forms.Padding(0, 4, 0, 0);
            this.folderView1.Size = new System.Drawing.Size(312, 286);
            this.folderView1.TabIndex = 2;
            // 
            // headerStrip1
            // 
            this.headerStrip1.AutoSize = false;
            this.headerStrip1.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.headerStrip1.ForeColor = System.Drawing.Color.Black;
            this.headerStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.headerStrip1.HeaderStyle = Web7.SVRN7.Apps.AreaHeaderStyle.Small;
            this.headerStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripLabel2});
            this.headerStrip1.Location = new System.Drawing.Point(0, 25);
            this.headerStrip1.Name = "headerStrip1";
            this.headerStrip1.Size = new System.Drawing.Size(312, 19);
            this.headerStrip1.TabIndex = 0;
            this.headerStrip1.Text = "headerStrip1";
            // 
            // toolStripLabel2
            // 
            this.toolStripLabel2.Name = "toolStripLabel2";
            this.toolStripLabel2.Size = new System.Drawing.Size(80, 16);
            this.toolStripLabel2.Text = " All Mail Folders";
            // 
            // headerStrip2
            // 
            this.headerStrip2.AutoSize = false;
            this.headerStrip2.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.headerStrip2.ForeColor = System.Drawing.Color.White;
            this.headerStrip2.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.headerStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripLabel1});
            this.headerStrip2.Location = new System.Drawing.Point(0, 0);
            this.headerStrip2.Name = "headerStrip2";
            this.headerStrip2.Size = new System.Drawing.Size(312, 25);
            this.headerStrip2.TabIndex = 1;
            this.headerStrip2.Text = "headerStrip2";
            // 
            // toolStripLabel1
            // 
            this.toolStripLabel1.Name = "toolStripLabel1";
            this.toolStripLabel1.Size = new System.Drawing.Size(43, 22);
            this.toolStripLabel1.Text = " Mail";
            // 
            // stackStrip
            // 
            this.stackStrip.AutoSize = false;
            this.stackStrip.CanOverflow = false;
            this.stackStrip.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stackStrip.Font = new System.Drawing.Font("Tahoma", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.World);
            this.stackStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.stackStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.stackStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton1,
            this.toolStripButton2,
            this.toolStripButton3,
            this.toolStripButton4,
            this.toolStripButton5,
            this.toolStripButton6,
            this.toolStripButton7});
            this.stackStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.VerticalStackWithOverflow;
            this.stackStrip.Location = new System.Drawing.Point(0, 0);
            this.stackStrip.Name = "stackStrip";
            this.stackStrip.Padding = new System.Windows.Forms.Padding(0);
            this.stackStrip.Size = new System.Drawing.Size(312, 260);
            this.stackStrip.TabIndex = 0;
            this.stackStrip.Tag = "Read";
            this.stackStrip.Text = "stackStrip1";
            // 
            // toolStripButton1
            // 
            this.toolStripButton1.Checked = true;
            this.toolStripButton1.CheckOnClick = true;
            this.toolStripButton1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.toolStripButton1.Image = global::Web7.SVRN7.Apps.Properties.Resources.Mail24;
            this.toolStripButton1.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toolStripButton1.ImageTransparentColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(238)))), ((int)(((byte)(238)))));
            this.toolStripButton1.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripButton1.Name = "toolStripButton1";
            this.toolStripButton1.Padding = new System.Windows.Forms.Padding(2);
            this.toolStripButton1.Size = new System.Drawing.Size(311, 32);
            this.toolStripButton1.Tag = "Read";
            this.toolStripButton1.Text = "Mail";
            this.toolStripButton1.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // toolStripButton2
            // 
            this.toolStripButton2.CheckOnClick = true;
            this.toolStripButton2.Image = global::Web7.SVRN7.Apps.Properties.Resources.Calendar24;
            this.toolStripButton2.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toolStripButton2.ImageTransparentColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(238)))), ((int)(((byte)(238)))));
            this.toolStripButton2.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripButton2.Name = "toolStripButton2";
            this.toolStripButton2.Padding = new System.Windows.Forms.Padding(2);
            this.toolStripButton2.Size = new System.Drawing.Size(311, 32);
            this.toolStripButton2.Tag = "NewAppointment";
            this.toolStripButton2.Text = "Calendar";
            this.toolStripButton2.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // toolStripButton3
            // 
            this.toolStripButton3.CheckOnClick = true;
            this.toolStripButton3.Image = global::Web7.SVRN7.Apps.Properties.Resources.Contacts24;
            this.toolStripButton3.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toolStripButton3.ImageTransparentColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(238)))), ((int)(((byte)(238)))));
            this.toolStripButton3.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripButton3.Name = "toolStripButton3";
            this.toolStripButton3.Padding = new System.Windows.Forms.Padding(2);
            this.toolStripButton3.Size = new System.Drawing.Size(311, 32);
            this.toolStripButton3.Tag = "NewContact";
            this.toolStripButton3.Text = "Contacts";
            this.toolStripButton3.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // toolStripButton4
            // 
            this.toolStripButton4.CheckOnClick = true;
            this.toolStripButton4.Image = global::Web7.SVRN7.Apps.Properties.Resources.Tasks24;
            this.toolStripButton4.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toolStripButton4.ImageTransparentColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(238)))), ((int)(((byte)(238)))));
            this.toolStripButton4.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripButton4.Name = "toolStripButton4";
            this.toolStripButton4.Padding = new System.Windows.Forms.Padding(2);
            this.toolStripButton4.Size = new System.Drawing.Size(311, 32);
            this.toolStripButton4.Tag = "NewTask";
            this.toolStripButton4.Text = "Notifications";
            this.toolStripButton4.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // toolStripButton5
            // 
            this.toolStripButton5.CheckOnClick = true;
            this.toolStripButton5.Image = global::Web7.SVRN7.Apps.Properties.Resources.Notes32;
            this.toolStripButton5.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toolStripButton5.ImageTransparentColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(238)))), ((int)(((byte)(238)))));
            this.toolStripButton5.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripButton5.Name = "toolStripButton5";
            this.toolStripButton5.Padding = new System.Windows.Forms.Padding(2);
            this.toolStripButton5.Size = new System.Drawing.Size(311, 32);
            this.toolStripButton5.Tag = "Notes";
            this.toolStripButton5.Text = "Notes";
            this.toolStripButton5.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // toolStripButton6
            // 
            this.toolStripButton6.CheckOnClick = true;
            this.toolStripButton6.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton6.Image")));
            this.toolStripButton6.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toolStripButton6.ImageTransparentColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(238)))), ((int)(((byte)(238)))));
            this.toolStripButton6.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripButton6.Name = "toolStripButton6";
            this.toolStripButton6.Padding = new System.Windows.Forms.Padding(2);
            this.toolStripButton6.Size = new System.Drawing.Size(311, 32);
            this.toolStripButton6.Tag = "Folder";
            this.toolStripButton6.Text = "Folders";
            this.toolStripButton6.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // toolStripButton7
            // 
            this.toolStripButton7.CheckOnClick = true;
            this.toolStripButton7.Image = global::Web7.SVRN7.Apps.Properties.Resources.Shortcut24;
            this.toolStripButton7.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.toolStripButton7.ImageTransparentColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(238)))), ((int)(((byte)(238)))));
            this.toolStripButton7.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripButton7.Name = "toolStripButton7";
            this.toolStripButton7.Padding = new System.Windows.Forms.Padding(2);
            this.toolStripButton7.Size = new System.Drawing.Size(311, 32);
            this.toolStripButton7.Tag = "Shortcut";
            this.toolStripButton7.Text = "Shortcuts";
            this.toolStripButton7.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // overflowStrip
            // 
            this.overflowStrip.AutoSize = false;
            this.overflowStrip.CanOverflow = false;
            this.overflowStrip.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.overflowStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.overflowStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripDropDownButton1});
            this.overflowStrip.Location = new System.Drawing.Point(0, 260);
            this.overflowStrip.Name = "overflowStrip";
            this.overflowStrip.Size = new System.Drawing.Size(312, 32);
            this.overflowStrip.TabIndex = 1;
            this.overflowStrip.Text = "overflowStrip";
            // 
            // toolStripDropDownButton1
            // 
            this.toolStripDropDownButton1.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.toolStripDropDownButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.None;
            this.toolStripDropDownButton1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.addorRemoveButtonsToolStripMenuItem});
            this.toolStripDropDownButton1.Image = ((System.Drawing.Image)(resources.GetObject("toolStripDropDownButton1.Image")));
            this.toolStripDropDownButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripDropDownButton1.Margin = new System.Windows.Forms.Padding(0);
            this.toolStripDropDownButton1.Name = "toolStripDropDownButton1";
            this.toolStripDropDownButton1.Padding = new System.Windows.Forms.Padding(3);
            this.toolStripDropDownButton1.Size = new System.Drawing.Size(19, 32);
            this.toolStripDropDownButton1.Text = "toolStripDropDownButton1";
            // 
            // addorRemoveButtonsToolStripMenuItem
            // 
            this.addorRemoveButtonsToolStripMenuItem.Name = "addorRemoveButtonsToolStripMenuItem";
            this.addorRemoveButtonsToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            this.addorRemoveButtonsToolStripMenuItem.Text = "&Add or Remove Buttons";
            // 
            // LeftSpine
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Transparent;
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.stackStripSplitter);
            this.Name = "LeftSpine";
            this.Size = new System.Drawing.Size(312, 629);
            this.Load += new System.EventHandler(this.StackBar_Load);
            this.stackStripSplitter.Panel1.ResumeLayout(false);
            this.stackStripSplitter.Panel2.ResumeLayout(false);
            this.stackStripSplitter.ResumeLayout(false);
            this.headerStrip1.ResumeLayout(false);
            this.headerStrip1.PerformLayout();
            this.headerStrip2.ResumeLayout(false);
            this.headerStrip2.PerformLayout();
            this.stackStrip.ResumeLayout(false);
            this.stackStrip.PerformLayout();
            this.overflowStrip.ResumeLayout(false);
            this.overflowStrip.PerformLayout();
            this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.SplitContainer stackStripSplitter;
		private FolderView folderView1;
		private HeaderStrip headerStrip1;
		private System.Windows.Forms.ToolStripLabel toolStripLabel2;
		private HeaderStrip headerStrip2;
		private System.Windows.Forms.ToolStripLabel toolStripLabel1;
		private StackStrip stackStrip;
		private System.Windows.Forms.ToolStripButton toolStripButton1;
		private System.Windows.Forms.ToolStripButton toolStripButton2;
		private System.Windows.Forms.ToolStripButton toolStripButton3;
		private System.Windows.Forms.ToolStripButton toolStripButton4;
		private System.Windows.Forms.ToolStripButton toolStripButton5;
		private System.Windows.Forms.ToolStripButton toolStripButton6;
		private System.Windows.Forms.ToolStripButton toolStripButton7;
		private BaseStackStrip overflowStrip;
		private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton1;
		private System.Windows.Forms.ToolStripMenuItem addorRemoveButtonsToolStripMenuItem;
	}
}
