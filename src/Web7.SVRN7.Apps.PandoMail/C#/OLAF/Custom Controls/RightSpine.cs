using System;
using System.IO;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Windows.Forms;

using System.Reflection;

using System.Drawing.Drawing2D;
using Microsoft.Web.WebView2.WinForms;

namespace Web7.SVRN7.Apps
{
	/// <summary>
	/// Summary description for MessageView.
	/// </summary>
	public class RightSpine : System.Windows.Forms.UserControl
	{
		private System.Windows.Forms.Panel panel1;

		private WebView2 webView1;

		private System.Windows.Forms.Panel panel2;

		private System.Windows.Forms.Label fromLabel;

		private System.Windows.Forms.Label subjectLabel;

		private System.Windows.Forms.Label toText;

		private System.Windows.Forms.Label ccText;

		private System.Windows.Forms.Label repliedLabel;

		private System.Windows.Forms.Label label3;

		private System.Windows.Forms.Label ccLabel;

		private System.Windows.Forms.Label toLabel;
		private TableLayoutPanel tableLayoutPanel1;

		private System.ComponentModel.IContainer components = null;
		private Assembly _assembly;
		private string _namePrefix;
		private bool _loaded = false;
		private MessageStore _store = null;
		private MailMessage _message = null;
		private string _pendingHtml = null;

		public RightSpine()
		{
			// This call is required by the Windows.Forms Form Designer.
			InitializeComponent();

			// TODO: Add any initialization after the InitializeComponent call
            Type type = this.GetType();
            _assembly = type.Assembly;
            string[] allResources = _assembly.GetManifestResourceNames();
            string sample = Array.Find(allResources, r => r.EndsWith(".Mail.Inbox.xml"));
            _namePrefix = (sample != null) ? sample.Substring(0, sample.Length - "Inbox.xml".Length) : type.Namespace + ".Mail.";
		}

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (components != null)
				{
					components.Dispose();
				}
			}

			base.Dispose(disposing);
		}

		/// <summary> 
		/// Required designer variable.
		/// </summary>
		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>

		#region Component Designer generated code
		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		// private void InitializeComponent()
		// {
		private void InitializeComponent()
		{
			this.panel1 = new System.Windows.Forms.Panel();
			this.webView1 = new WebView2();
			this.panel2 = new System.Windows.Forms.Panel();
			this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
			this.toText = new System.Windows.Forms.Label();
			this.toLabel = new System.Windows.Forms.Label();
			this.ccLabel = new System.Windows.Forms.Label();
			this.ccText = new System.Windows.Forms.Label();
			this.label3 = new System.Windows.Forms.Label();
			this.repliedLabel = new System.Windows.Forms.Label();
			this.fromLabel = new System.Windows.Forms.Label();
			this.subjectLabel = new System.Windows.Forms.Label();
			this.panel1.SuspendLayout();
			this.panel2.SuspendLayout();
			this.tableLayoutPanel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.panel1.BackColor = System.Drawing.SystemColors.Window;
			this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.panel1.Controls.Add(this.webView1);
			this.panel1.Controls.Add(this.panel2);
			this.panel1.Location = new System.Drawing.Point(6, 6);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(444, 329);
			this.panel1.TabIndex = 0;
			//
			// webView1
			//
			this.webView1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.webView1.Location = new System.Drawing.Point(0, 126);
			this.webView1.Name = "webView1";
			this.webView1.Size = new System.Drawing.Size(442, 201);
			// 
			// panel2
			// 
			this.panel2.Controls.Add(this.tableLayoutPanel1);
			this.panel2.Controls.Add(this.label3);
			this.panel2.Controls.Add(this.repliedLabel);
			this.panel2.Controls.Add(this.fromLabel);
			this.panel2.Controls.Add(this.subjectLabel);
			this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel2.Location = new System.Drawing.Point(0, 0);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(442, 126);
			this.panel2.TabIndex = 0;
			// 
			// tableLayoutPanel1
			// 
			this.tableLayoutPanel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tableLayoutPanel1.AutoSize = true;
			this.tableLayoutPanel1.ColumnCount = 3;
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayoutPanel1.Controls.Add(this.toText, 0, 0);
			this.tableLayoutPanel1.Controls.Add(this.toLabel, 1, 0);
			this.tableLayoutPanel1.Controls.Add(this.ccLabel, 1, 1);
			this.tableLayoutPanel1.Controls.Add(this.ccText, 0, 1);
			this.tableLayoutPanel1.Location = new System.Drawing.Point(6, 79);
			this.tableLayoutPanel1.Name = "tableLayoutPanel1";
			this.tableLayoutPanel1.RowCount = 3;
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.Size = new System.Drawing.Size(426, 38);
			this.tableLayoutPanel1.TabIndex = 8;
			// 
			// toText
			// 
			this.toText.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.toText.AutoSize = true;
			this.toText.Font = new System.Drawing.Font("Arial", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.toText.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(144)))), ((int)(((byte)(153)))), ((int)(((byte)(174)))));
			this.toText.Location = new System.Drawing.Point(0, 0);
			this.toText.Margin = new System.Windows.Forms.Padding(0);
			this.toText.Name = "toText";
			this.toText.Padding = new System.Windows.Forms.Padding(1);
			this.toText.Size = new System.Drawing.Size(26, 16);
			this.toText.TabIndex = 3;
			this.toText.Text = "To:";
			// 
			// toLabel
			// 
			this.toLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.toLabel.AutoSize = true;
			this.toLabel.Font = new System.Drawing.Font("Arial", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.toLabel.Location = new System.Drawing.Point(26, 0);
			this.toLabel.Margin = new System.Windows.Forms.Padding(0);
			this.toLabel.Name = "toLabel";
			this.toLabel.Padding = new System.Windows.Forms.Padding(6, 1, 1, 1);
			this.toLabel.Size = new System.Drawing.Size(76, 16);
			this.toLabel.TabIndex = 6;
			this.toLabel.Text = "";
			// 
			// ccLabel
			// 
			this.ccLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.ccLabel.AutoSize = true;
			this.ccLabel.Font = new System.Drawing.Font("Arial", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.ccLabel.Location = new System.Drawing.Point(26, 16);
			this.ccLabel.Margin = new System.Windows.Forms.Padding(0);
			this.ccLabel.Name = "ccLabel";
			this.ccLabel.Padding = new System.Windows.Forms.Padding(6, 1, 1, 1);
			this.ccLabel.Size = new System.Drawing.Size(131, 16);
			this.ccLabel.TabIndex = 7;
			this.ccLabel.Text = "";
			// 
			// ccText
			// 
			this.ccText.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.ccText.AutoSize = true;
			this.ccText.Font = new System.Drawing.Font("Arial", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.ccText.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(144)))), ((int)(((byte)(153)))), ((int)(((byte)(174)))));
			this.ccText.Location = new System.Drawing.Point(0, 16);
			this.ccText.Margin = new System.Windows.Forms.Padding(0);
			this.ccText.Name = "ccText";
			this.ccText.Padding = new System.Windows.Forms.Padding(1);
			this.ccText.Size = new System.Drawing.Size(26, 16);
			this.ccText.TabIndex = 4;
			this.ccText.Text = "Cc:";
			// 
			// label3
			// 
			this.label3.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.label3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(172)))), ((int)(((byte)(168)))), ((int)(((byte)(153)))));
			this.label3.Location = new System.Drawing.Point(10, 120);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(422, 1);
			this.label3.TabIndex = 5;
			// 
			// repliedLabel
			// 
			this.repliedLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.repliedLabel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(144)))), ((int)(((byte)(153)))), ((int)(((byte)(173)))));
			this.repliedLabel.Font = new System.Drawing.Font("Arial", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.repliedLabel.ForeColor = System.Drawing.SystemColors.Window;
			this.repliedLabel.Location = new System.Drawing.Point(10, 58);
			this.repliedLabel.Name = "repliedLabel";
			this.repliedLabel.Size = new System.Drawing.Size(422, 16);
			this.repliedLabel.TabIndex = 2;
			this.repliedLabel.Text = "";
			this.repliedLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			// 
			// fromLabel
			// 
			this.fromLabel.AutoSize = true;
			this.fromLabel.Font = new System.Drawing.Font("Arial", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.fromLabel.Location = new System.Drawing.Point(10, 35);
			this.fromLabel.Name = "fromLabel";
			this.fromLabel.Size = new System.Drawing.Size(89, 17);
			this.fromLabel.TabIndex = 1;
			this.fromLabel.Text = "";
			// 
			// subjectLabel
			// 
			this.subjectLabel.AutoSize = true;
			this.subjectLabel.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.subjectLabel.Location = new System.Drawing.Point(10, 10);
			this.subjectLabel.Name = "subjectLabel";
			this.subjectLabel.Size = new System.Drawing.Size(103, 19);
			this.subjectLabel.TabIndex = 0;
			this.subjectLabel.Text = "";
			// 
			// RightSpine
			// 
			this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.Controls.Add(this.panel1);
			this.Name = "RightSpine";
			this.Size = new System.Drawing.Size(456, 341);
			this.Load += new System.EventHandler(this.MessageView_Load);
			this.panel1.ResumeLayout(false);
			this.panel2.ResumeLayout(false);
			this.panel2.PerformLayout();
			this.tableLayoutPanel1.ResumeLayout(false);
			this.tableLayoutPanel1.PerformLayout();
			this.ResumeLayout(false);

		}
		#endregion

		#region Properties
		[DefaultValue(null)]
		private MailMessage Message
		{
			get
			{
				return _message;
			}
			set
			{
				_message = value;

				if (!_loaded) return;

				if (value != null)
				{
					this.subjectLabel.Text = _message.Subject ?? string.Empty;
					this.fromLabel.Text = _message.From ?? string.Empty;
					this.toLabel.Text = _message.To ?? string.Empty;
					this.ccLabel.Text = _message.Cc ?? string.Empty;

					if (_message.Subject != null && _message.Subject.Contains("RE:"))
					{
						DateTime time = _message.SentDate;
						this.repliedLabel.Text = " You replied to this message on " + time.Subtract(new TimeSpan(3, 3, 3)).ToShortDateString();
					}
					else
					{
						this.repliedLabel.Text = " You have not responded to this message";
					}

                    if (null != _assembly)
                    {
                        Stream doc = _assembly.GetManifestResourceStream(_namePrefix + _message.Path);

                        if (null != doc)
                        {
                            string html;
                            using (var reader = new StreamReader(doc))
                                html = reader.ReadToEnd();

                            if (webView1.CoreWebView2 != null)
                                webView1.NavigateToString(html);
                            else
                                _pendingHtml = html;
                        }
                    }
				}
				else
				{
					this.subjectLabel.Text = string.Empty;
					this.fromLabel.Text = string.Empty;
					this.toLabel.Text = string.Empty;
					this.ccLabel.Text = string.Empty;
					this.repliedLabel.Text = string.Empty;
					_pendingHtml = null;
					if (webView1.CoreWebView2 != null)
						webView1.NavigateToString("<html><body></body></html>");
				}
			}
		}
		#endregion

		#region Event Handlers
		protected override void OnPaint(PaintEventArgs e)
		{
			// Paint gradient
			LinearGradientBrush brush = new LinearGradientBrush(this.ClientRectangle, Color.FromArgb(106, 112, 128), Color.FromArgb(138, 146, 166), LinearGradientMode.ForwardDiagonal);

			e.Graphics.FillRectangle(brush, this.ClientRectangle);

			// Call base
			base.OnPaint(e);
		}

		void MessageStore_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (String.IsNullOrEmpty(e.PropertyName) || (e.PropertyName == "SelectedMessage"))
			{
				this.Message = _store.SelectedMessage;
			}
		}

		private async void MessageView_Load(object sender, EventArgs e)
		{
			// Set Dock Stlye
			this.Dock = DockStyle.Fill;

			// Set loaded flag
			_loaded = true;

			// Load the Message (if not in design mode)
			if (null == this.Site)
			{
				_store = MessageStore.GetMessageStore();

                if (null != _store)
                {
                    // Hook change notification before setting Message so we never miss an update.
                    _store.PropertyChanged += new PropertyChangedEventHandler(MessageStore_PropertyChanged);

                    // Get Current — CoreWebView2 not yet initialized; setter stores pending HTML.
                    this.Message = _store.SelectedMessage;
                }

				// Initialize WebView2; dispatch any HTML that was set before init completed
				await webView1.EnsureCoreWebView2Async(null);
				if (_pendingHtml != null)
				{
					webView1.NavigateToString(_pendingHtml);
					_pendingHtml = null;
				}
			}
			else if ((null != this.Parent) && (this.Parent.Site != null) && (this.Parent.Site.DesignMode == true))
			{
				this.webView1.Visible = false;
			}

			// Set parent padding
			if (null != this.Parent)
			{
				this.Parent.Padding = new Padding(0, 3, 3, 3);
			}
		}
		#endregion
	}
}
