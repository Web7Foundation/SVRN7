using Pando.Board.Controls;
using Pando.Board.Models;
using Pando.Board.Services;
using Message = Pando.Board.Models.Message;

namespace Pando.Board.Forms;

public class BoardForm : Form
{
    private readonly VelocityService _velocity;
    private BoardSurface _surface = null!;
    private List<BoardColumn> _columns = new();

    // Top bar controls
    private Panel _topBar = null!;
    private Label _titleLabel = null!;
    private Label _taglineLabel = null!;
    private TrackBar _speedSlider = null!;
    private Label _speedLabel = null!;
    private Label _statusLabel = null!;
    private System.Windows.Forms.Timer _velocityTimer = null!;

    public BoardForm(VelocityService velocity)
    {
        _velocity = velocity;
        InitializeForm();
        InitializeSurface();
        LoadData();
        StartVelocityTimer();
    }

    private void InitializeForm()
    {
        Text = "Pando Board — Personal Intelligence Surface";
        BackColor = Color.FromArgb(8, 12, 22);
        ForeColor = Color.FromArgb(232, 234, 240);
        Size = new Size(1400, 900);
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        // ── Top bar ──
        _topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 75,
            BackColor = Color.FromArgb(15, 20, 32),
            Padding = new Padding(12, 0, 12, 0)
        };

        // Logo
        var logoBorder = new Panel
        {
            Size = new Size(32, 32),
            Location = new Point(12, 8),
            BackColor = Color.FromArgb(79, 142, 247)
        };
        var logoLabel = new Label
        {
            Text = "PB",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        logoBorder.Controls.Add(logoLabel);
        _topBar.Controls.Add(logoBorder);

        // Title
        _titleLabel = new Label
        {
            Text = "Pando Board",
            ForeColor = Color.FromArgb(232, 234, 240),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Location = new Point(52, 8),
            Size = new Size(200, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _topBar.Controls.Add(_titleLabel);


        // Speed slider
        _speedLabel = new Label
        {
            Text = "Transition Speed",
            ForeColor = Color.FromArgb(74, 80, 112),
            Font = new Font("Segoe UI", 8f),
            Size = new Size(110, 14),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _topBar.Controls.Add(_speedLabel);

        _speedSlider = new TrackBar
        {
            Minimum = 1,
            Maximum = 10,
            Value = 5,
            TickFrequency = 1,
            Size = new Size(100, 32),
            BackColor = Color.FromArgb(15, 20, 32)
        };
        _speedSlider.ValueChanged += (_, _) =>
        {
            float speed = _speedSlider.Value / 25f;
            _surface?.SetAnimSpeed(speed);
        };
        _topBar.Controls.Add(_speedSlider);

        // Status bar (bottom)
        var statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            BackColor = Color.FromArgb(15, 20, 32)
        };
        _statusLabel = new Label
        {
            Text = "● TDA connected  |  DIDComm V2 · SignThenEncrypt · did:drn verified  |  did:drn:svrn7.net:michael#pandochat",
            ForeColor = Color.FromArgb(74, 80, 112),
            Font = new Font("Consolas", 8f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0)
        };
        statusBar.Controls.Add(_statusLabel);

        Controls.Add(_topBar);
        Controls.Add(statusBar);

        Resize += (_, _) => PositionTopBarControls();
        PositionTopBarControls();
    }

    private void PositionTopBarControls()
    {
        int right = _topBar.Width - 12;
        _speedSlider.Location = new Point(right - 100, 8);
        _speedLabel.Location = new Point(right - 215, 17);
    }

    private void InitializeSurface()
    {
        _surface = new BoardSurface(_velocity)
        {
            Dock = DockStyle.Fill,
            TabStop = true
        };

        _surface.ColumnActivated += col =>
        {
            // Column activation handled internally by BoardSurface
            Invoke(Invalidate);
        };

        _surface.MessageSelected += (col, msg) =>
        {
            // Message selected — could update a detail pane in future
        };

        _surface.MessageDroppedOnSpine += (sourceMsg, targetCol) =>
        {
            AppendDroppedMessage(sourceMsg.Content, targetCol);
        };

        _surface.MessageDroppedOnMessage += (sourceMsg, targetMsg, targetCol) =>
        {
            AppendDroppedMessage(sourceMsg.Content, targetCol);
        };

        _surface.MessageDroppedOnComposer += (sourceMsg, targetCol) =>
        {
            AppendDroppedMessage(sourceMsg.Content, targetCol);
        };

        _surface.NavigateLeftRequested += () => _surface.NavigateLeft();
        _surface.NavigateRightRequested += () => _surface.NavigateRight();

        Controls.Add(_surface);
        _surface.BringToFront();
        _surface.Focus();
    }

    private void AppendDroppedMessage(string content, BoardColumn targetCol)
    {
        var thread = targetCol.ActiveThread;
        if (thread == null)
        {
            var thid = Guid.NewGuid().ToString();
            thread = new ConversationThread { Thid = thid };
            targetCol.Contact.Threads.Add(thread);
            targetCol.ActiveThread = thread;
        }
        var msg = new Message
        {
            Id = Guid.NewGuid().ToString(),
            Thid = thread.Thid,
            Content = ">" + content,
            SenderDid = "did:drn:svrn7.net:michael#pandochat",
            IsOutbound = true,
            Timestamp = DateTime.UtcNow
        };
        thread.Messages.Add(msg);
        targetCol.SelectedMessage = msg;
        targetCol.ScrollOffset = float.MaxValue;
        _surface.SetActiveColumn(targetCol);
        _surface.Invalidate();
    }

    private void LoadData()
    {
        _columns = SeedDataService.CreateSeedColumns();
        _columns = _velocity.RefreshAndSort(_columns);
        _surface.SetColumns(_columns);
    }

    private void StartVelocityTimer()
    {
        _velocityTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _velocityTimer.Tick += (_, _) =>
        {
            _columns = _velocity.RefreshAndSort(_columns);
            _surface.SetColumns(_columns);
        };
        _velocityTimer.Start();
    }
}
