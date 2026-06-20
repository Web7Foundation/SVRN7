using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Web7.SVRN7.Apps
{
    /// <summary>
    /// Compose window modelled on the RightSpine reading-pane style:
    /// slate-blue diagonal gradient surround, white bordered inner panel,
    /// header fields (To / Cc / Subject) above a Chromium (WebView2)
    /// rich-text editor body.
    /// </summary>
    public class NewMailMessageForm : Form
    {
        private readonly TdaMailClient _client;

        private Panel panel1;
        private ToolStrip toolStrip1;
        private ToolStripButton btnSend;
        private Panel panel2;
        private TableLayoutPanel tableLayoutPanel1;
        private Label lblFromCaption;
        private TextBox txtFrom;
        private Label lblToCaption;
        private TextBox txtTo;
        private Label lblCcCaption;
        private TextBox txtCc;
        private Label lblSubjectCaption;
        private TextBox txtSubject;
        private Panel panelDivider;
        private WebView2 webView;

        // -----------------------------------------------------------------
        // Chromium contenteditable editor with formatting toolbar.
        // Toolbar commands use document.execCommand which is still fully
        // functional in Chromium/Edge even though the spec deprecated it.
        // -----------------------------------------------------------------
        private const string EditorHtml = @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
  *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

  body {
    font-family: Arial, sans-serif;
    font-size: 10pt;
    background: #ffffff;
    display: flex;
    flex-direction: column;
    height: 100vh;
    overflow: hidden;
  }

  /* ---- Toolbar ---- */
  #toolbar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 2px;
    padding: 3px 4px;
    background: #f4f4f4;
    border-bottom: 1px solid #acacac;
    flex-shrink: 0;
  }

  #toolbar button {
    min-width: 24px;
    height: 22px;
    padding: 0 5px;
    font-size: 12px;
    cursor: pointer;
    background: #ffffff;
    border: 1px solid #bbb;
    border-radius: 2px;
    line-height: 20px;
  }
  #toolbar button:hover  { background: #dce9f9; border-color: #6a9fd8; }
  #toolbar button.active { background: #c5d8f5; border-color: #4a7fc1; font-weight: bold; }

  #toolbar select {
    height: 22px;
    font-size: 11px;
    border: 1px solid #bbb;
    border-radius: 2px;
    background: #fff;
    cursor: pointer;
  }
  #fontName { width: 110px; }
  #fontSize { width:  60px; }

  #toolbar input[type=color] {
    width: 28px;
    height: 22px;
    padding: 1px 2px;
    cursor: pointer;
    border: 1px solid #bbb;
    border-radius: 2px;
    background: #fff;
  }

  .sep {
    width: 1px;
    height: 18px;
    background: #c8c8c8;
    margin: 0 2px;
    flex-shrink: 0;
  }

  /* ---- Editor ---- */
  #editor {
    flex: 1;
    outline: none;
    padding: 10px 12px;
    overflow-y: auto;
    font-family: Arial, sans-serif;
    font-size: 10pt;
    line-height: 1.5;
    caret-color: #000;
  }
  #editor:empty::before {
    content: attr(data-placeholder);
    color: #aaa;
    pointer-events: none;
  }
</style>
</head>
<body>

<div id='toolbar'>

  <!-- Font family -->
  <select id='fontName' title='Font family'
          onchange=""exec('fontName', this.value)"">
    <option value='Arial'          selected>Arial</option>
    <option value='Calibri'               >Calibri</option>
    <option value='Comic Sans MS'         >Comic Sans MS</option>
    <option value='Courier New'           >Courier New</option>
    <option value='Georgia'               >Georgia</option>
    <option value='Tahoma'                >Tahoma</option>
    <option value='Times New Roman'       >Times New Roman</option>
    <option value='Trebuchet MS'          >Trebuchet MS</option>
    <option value='Verdana'               >Verdana</option>
  </select>

  <!-- Font size  (execCommand uses 1-7 HTML sizes) -->
  <select id='fontSize' title='Font size'
          onchange=""exec('fontSize', this.value)"">
    <option value='1'>8pt</option>
    <option value='2'>10pt</option>
    <option value='3' selected>12pt</option>
    <option value='4'>14pt</option>
    <option value='5'>18pt</option>
    <option value='6'>24pt</option>
    <option value='7'>36pt</option>
  </select>

  <div class='sep'></div>

  <!-- Style -->
  <button id='btnBold'      onclick=""exec('bold')""        title='Bold (Ctrl+B)'><b>B</b></button>
  <button id='btnItalic'    onclick=""exec('italic')""      title='Italic (Ctrl+I)'><i>I</i></button>
  <button id='btnUnderline' onclick=""exec('underline')""   title='Underline (Ctrl+U)'><u>U</u></button>
  <button id='btnStrike'    onclick=""exec('strikeThrough')"" title='Strikethrough'><s>S</s></button>

  <div class='sep'></div>

  <!-- Colour -->
  <input type='color' id='foreColor' value='#000000'
         onchange=""exec('foreColor',  this.value)""
         title='Text colour'>
  <input type='color' id='hiliteColor' value='#ffff00'
         onchange=""exec('hiliteColor', this.value)""
         title='Highlight colour'>

  <div class='sep'></div>

  <!-- Alignment -->
  <button onclick=""exec('justifyLeft')""   title='Align left'>&#8676;</button>
  <button onclick=""exec('justifyCenter')"" title='Centre'>&#8596;</button>
  <button onclick=""exec('justifyRight')""  title='Align right'>&#8677;</button>

  <div class='sep'></div>

  <!-- Lists -->
  <button onclick=""exec('insertUnorderedList')"" title='Bullet list'>&#8226;</button>
  <button onclick=""exec('insertOrderedList')""   title='Numbered list'>1.</button>

  <div class='sep'></div>

  <!-- Indent -->
  <button onclick=""exec('indent')""  title='Increase indent'>&#8677;&#8677;</button>
  <button onclick=""exec('outdent')"" title='Decrease indent'>&#8676;&#8676;</button>

  <div class='sep'></div>

  <!-- Clear -->
  <button onclick=""exec('removeFormat')"" title='Remove formatting'
          style='color:#800000;'>&#x2715;</button>

</div>

<div id='editor'
     contenteditable='true'
     data-placeholder='Type your message here…'
     spellcheck='true'></div>

<script>
  function exec(cmd, val) {
    document.execCommand(cmd, false, val != null ? val : null);
    document.getElementById('editor').focus();
    updateToolbar();
  }

  function updateToolbar() {
    toggle('btnBold',      'bold');
    toggle('btnItalic',    'italic');
    toggle('btnUnderline', 'underline');
    toggle('btnStrike',    'strikeThrough');
  }

  function toggle(id, cmd) {
    var el = document.getElementById(id);
    if (el) el.classList.toggle('active', document.queryCommandState(cmd));
  }

  var editor = document.getElementById('editor');
  editor.addEventListener('keyup',   updateToolbar);
  editor.addEventListener('mouseup', updateToolbar);
  document.addEventListener('selectionchange', updateToolbar);
  editor.focus();
</script>
</body>
</html>";

        public NewMailMessageForm(TdaMailClient client)
        {
            _client = client;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.panel1             = new Panel();
            this.toolStrip1         = new ToolStrip();
            this.btnSend            = new ToolStripButton();
            this.panel2             = new Panel();
            this.tableLayoutPanel1  = new TableLayoutPanel();
            this.lblFromCaption     = new Label();
            this.txtFrom            = new TextBox();
            this.lblToCaption       = new Label();
            this.txtTo              = new TextBox();
            this.lblCcCaption       = new Label();
            this.txtCc              = new TextBox();
            this.lblSubjectCaption  = new Label();
            this.txtSubject         = new TextBox();
            this.panelDivider       = new Panel();
            this.webView            = new WebView2();

            this.panel1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.webView).BeginInit();
            this.SuspendLayout();

            // -----------------------------------------------------------------
            // Caption label style — matches RightSpine's To:/Cc: label style
            // -----------------------------------------------------------------
            Font  captionFont  = new Font("Arial", 8.25F, FontStyle.Bold, GraphicsUnit.Point, 0);
            Color captionColor = Color.FromArgb(144, 153, 174);

            // btnSend
            this.btnSend.Image                 = Web7.SVRN7.Apps.Properties.Resources.Send;
            this.btnSend.ImageTransparentColor = Color.FromArgb(238, 238, 238);
            this.btnSend.Name                  = "btnSend";
            this.btnSend.Text                  = "Send";
            this.btnSend.DisplayStyle          = ToolStripItemDisplayStyle.ImageAndText;
            this.btnSend.Click                += new EventHandler(btnSend_Click);

            // toolStrip1
            this.toolStrip1.Dock      = DockStyle.Top;
            this.toolStrip1.GripStyle = ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new ToolStripItem[] { this.btnSend });
            this.toolStrip1.Name      = "toolStrip1";

            // lblFromCaption
            this.lblFromCaption.AutoSize  = true;
            this.lblFromCaption.Font      = captionFont;
            this.lblFromCaption.ForeColor = captionColor;
            this.lblFromCaption.Anchor    = AnchorStyles.Left;
            this.lblFromCaption.Margin    = new Padding(4, 5, 0, 0);
            this.lblFromCaption.Name      = "lblFromCaption";
            this.lblFromCaption.Text      = "From:";

            // txtFrom — read-only, pre-populated with TDA agent DID (with display name if available)
            this.txtFrom.Dock      = DockStyle.Fill;
            this.txtFrom.Margin    = new Padding(2, 4, 4, 2);
            this.txtFrom.Name      = "txtFrom";
            this.txtFrom.ReadOnly  = true;
            this.txtFrom.BackColor = SystemColors.Control;
            this.txtFrom.Text      = !string.IsNullOrEmpty(_client.TdaName) && !string.IsNullOrEmpty(_client.TdaDid)
                ? $"\"{_client.TdaName}\" <{_client.TdaDid}>"
                : _client.TdaDid;

            // lblToCaption
            this.lblToCaption.AutoSize  = true;
            this.lblToCaption.Font      = captionFont;
            this.lblToCaption.ForeColor = captionColor;
            this.lblToCaption.Anchor    = AnchorStyles.Left;
            this.lblToCaption.Margin    = new Padding(4, 5, 0, 0);
            this.lblToCaption.Name      = "lblToCaption";
            this.lblToCaption.Text      = "To:";

            // txtTo
            this.txtTo.Dock   = DockStyle.Fill;
            this.txtTo.Margin = new Padding(2, 4, 4, 2);
            this.txtTo.Name   = "txtTo";

            // lblCcCaption
            this.lblCcCaption.AutoSize  = true;
            this.lblCcCaption.Font      = captionFont;
            this.lblCcCaption.ForeColor = captionColor;
            this.lblCcCaption.Anchor    = AnchorStyles.Left;
            this.lblCcCaption.Margin    = new Padding(4, 5, 0, 0);
            this.lblCcCaption.Name      = "lblCcCaption";
            this.lblCcCaption.Text      = "Cc:";

            // txtCc
            this.txtCc.Dock   = DockStyle.Fill;
            this.txtCc.Margin = new Padding(2, 4, 4, 2);
            this.txtCc.Name   = "txtCc";

            // lblSubjectCaption
            this.lblSubjectCaption.AutoSize  = true;
            this.lblSubjectCaption.Font      = captionFont;
            this.lblSubjectCaption.ForeColor = captionColor;
            this.lblSubjectCaption.Anchor    = AnchorStyles.Left;
            this.lblSubjectCaption.Margin    = new Padding(4, 5, 0, 0);
            this.lblSubjectCaption.Name      = "lblSubjectCaption";
            this.lblSubjectCaption.Text      = "Subject:";

            // txtSubject — bold to echo RightSpine's subjectLabel appearance
            this.txtSubject.Dock   = DockStyle.Fill;
            this.txtSubject.Font   = new Font("Arial", 8.25F, FontStyle.Bold, GraphicsUnit.Point, 0);
            this.txtSubject.Margin = new Padding(2, 4, 4, 2);
            this.txtSubject.Name   = "txtSubject";

            // tableLayoutPanel1 — two-column grid: caption | field
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tableLayoutPanel1.RowCount = 4;
            this.tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            this.tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            this.tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            this.tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            this.tableLayoutPanel1.Controls.Add(this.lblFromCaption,    0, 0);
            this.tableLayoutPanel1.Controls.Add(this.txtFrom,           1, 0);
            this.tableLayoutPanel1.Controls.Add(this.lblToCaption,      0, 1);
            this.tableLayoutPanel1.Controls.Add(this.txtTo,             1, 1);
            this.tableLayoutPanel1.Controls.Add(this.lblCcCaption,      0, 2);
            this.tableLayoutPanel1.Controls.Add(this.txtCc,             1, 2);
            this.tableLayoutPanel1.Controls.Add(this.lblSubjectCaption, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.txtSubject,        1, 3);
            this.tableLayoutPanel1.Dock    = DockStyle.Fill;
            this.tableLayoutPanel1.Name    = "tableLayoutPanel1";
            this.tableLayoutPanel1.Padding = new Padding(4, 2, 4, 2);

            // panel2 — header-field container (Dock=Top, sits below toolStrip1)
            this.panel2.Controls.Add(this.tableLayoutPanel1);
            this.panel2.Dock   = DockStyle.Top;
            this.panel2.Height = 108;
            this.panel2.Name   = "panel2";

            // panelDivider — 1-px separator, matches RightSpine's label3
            this.panelDivider.BackColor = Color.FromArgb(172, 168, 153);
            this.panelDivider.Dock      = DockStyle.Top;
            this.panelDivider.Height    = 1;
            this.panelDivider.Name      = "panelDivider";

            // webView — Chromium body editor (initialised async in Load)
            this.webView.Dock = DockStyle.Fill;
            this.webView.Name = "webView";

            // panel1 — white inner panel, matches RightSpine's panel1
            // Dock order: Fill first, then Tops from bottom to top
            // (WinForms docks last-added Dock=Top at the topmost position)
            this.panel1.Controls.Add(this.webView);        // Dock=Fill  — fills body area
            this.panel1.Controls.Add(this.panelDivider);   // Dock=Top   — sits above body
            this.panel1.Controls.Add(this.panel2);         // Dock=Top   — sits above divider
            this.panel1.Controls.Add(this.toolStrip1);     // Dock=Top   — topmost strip
            this.panel1.BackColor   = SystemColors.Window;
            this.panel1.BorderStyle = BorderStyle.FixedSingle;
            this.panel1.Dock        = DockStyle.Fill;
            this.panel1.Name        = "panel1";

            // Form
            this.ClientSize    = new Size(628, 508);
            this.Controls.Add(this.panel1);
            this.Font          = SystemFonts.IconTitleFont;
            this.MinimumSize   = new Size(480, 360);
            this.Name          = "NewMailMessageForm";
            this.Padding       = new Padding(6);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text          = "New Mail Message";
            this.Load         += new EventHandler(NewMailMessageForm_Load);

            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.panel2.ResumeLayout(false);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.webView).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
        }

        // -----------------------------------------------------------------
        // Gradient background — matches RightSpine.OnPaint exactly
        // -----------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle r = this.ClientRectangle;
            if (r.Width > 0 && r.Height > 0)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    r,
                    Color.FromArgb(106, 112, 128),
                    Color.FromArgb(138, 146, 166),
                    LinearGradientMode.ForwardDiagonal))
                {
                    e.Graphics.FillRectangle(brush, r);
                }
            }
            base.OnPaint(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }

        // -----------------------------------------------------------------
        // WebView2 async initialisation — must complete before navigating
        // -----------------------------------------------------------------
        private async void NewMailMessageForm_Load(object sender, EventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async(null);
                webView.CoreWebView2.NavigateToString(EditorHtml);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The WebView2 Runtime is required for the rich-text editor.\n\n" +
                    "Download it from microsoft.com/edge/download (choose 'WebView2 Runtime').\n\n" +
                    "Detail: " + ex.Message,
                    "Web7 Mail — WebView2 not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async void btnSend_Click(object sender, EventArgs e)
        {
            string to      = txtTo.Text.Trim();
            string subject = txtSubject.Text.Trim();

            if (string.IsNullOrEmpty(to))
            {
                MessageBox.Show("Please enter a recipient DID in the To: field.",
                    "Web7 Mail", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTo.Focus();
                return;
            }

            if (string.IsNullOrEmpty(subject))
            {
                MessageBox.Show("Please enter a subject.",
                    "Web7 Mail", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtSubject.Focus();
                return;
            }

            string bodyText = string.Empty;
            if (webView.CoreWebView2 != null)
            {
                string json = await webView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('editor').innerHTML");
                bodyText = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(bodyText))
            {
                MessageBox.Show("Please enter a message body.",
                    "Web7 Mail", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                webView.Focus();
                return;
            }

            // Build sender display string from cached TDA identity
            string senderDisplay = !string.IsNullOrEmpty(_client.TdaName) && !string.IsNullOrEmpty(_client.TdaDid)
                ? $"\"{_client.TdaName}\" <{_client.TdaDid}>"
                : _client.TdaDid;

            // Resolve recipient DID Document to get display name; fall back to bare DID on miss/timeout
            string recipientDisplay = to;
            try
            {
                DidResolutionResult resolved = await _client.ResolveDidAsync(to);
                if (resolved.Found && !string.IsNullOrEmpty(resolved.Svrn7Name))
                    recipientDisplay = $"\"{resolved.Svrn7Name}\" <{to}>";
            }
            catch { }

            btnSend.Enabled = false;
            try
            {
                await _client.SendAsync(to, subject, bodyText, senderDisplay, recipientDisplay);
                MessageBox.Show("Mail message sent.", "Web7 Mail",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to send: is the Citizen TDA running on port " +
                    Program.TdaPort + "?\n\nDetail: " + ex.Message,
                    "Web7 Mail", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnSend.Enabled = true;
            }
        }
    }
}
