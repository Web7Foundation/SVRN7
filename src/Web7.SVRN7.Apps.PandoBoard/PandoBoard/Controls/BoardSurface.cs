using Pando.Board.Models;
using Pando.Board.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Message = Pando.Board.Models.Message;

namespace Pando.Board.Controls;

/// <summary>
/// Core Pando Board rendering surface.
/// Handles: column layout, bookshelf spine rendering, active column,
/// message list, hover expand, smooth animation, drag and drop, navigation.
/// </summary>
public class BoardSurface : SKControl
{
    // ── Services ──
    private readonly VelocityService _velocity;

    // ── State ──
    private List<BoardColumn> _columns = new();
    private Message? _draggingMessage;
    private Point _dragStartPoint;
    private bool _isDragging;
    private BoardColumn? _dropTargetColumn;
    private Message? _dropTargetMessage;
    private bool _dropOnComposer;
    private BoardColumn? _lastActiveCol;
    private Point _currentMousePos;
    private float _horizontalScrollOffset = 0f;

    // ── Composer overlay controls ──
    private TextBox _composerTextBox = null!;
    private Button _sendButton = null!;
    private float _skiaToLogicalX = 1f;
    private float _skiaToLogicalY = 1f;
    private bool _pendingComposerFocus = false;

    // ── Animation ──
    private System.Windows.Forms.Timer _animTimer;
    private float _animSpeed = 0.18f; // lerp factor per tick — dialable

    // ── Layout ──
    private const float TopBarHeight = 0f; // handled by form
    private const float ComposerHeight = 80f;
    private const float HeaderHeight = 60f;
    private const float MessagePadding = 12f;
    private const float BubbleRadius = 12f;
    private const float SpineWidth = BoardColumn.SpineWidth;
    private const float ActiveWidth = BoardColumn.ActiveWidth;

    // ── Fonts (SkiaSharp) ──
    private SKTypeface _sans = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
    private SKTypeface _sansBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
    private SKTypeface _mono = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal);

    // ── Colors ──
    private static readonly SKColor BgColor       = SKColor.Parse("#080C16");
    private static readonly SKColor Surface2      = SKColor.Parse("#161C2E");
    private static readonly SKColor Surface3      = SKColor.Parse("#1C2438");
    private static readonly SKColor TextPrimary   = SKColor.Parse("#E8EAF0");
    private static readonly SKColor TextDim       = SKColor.Parse("#8A90A8");
    private static readonly SKColor TextMuted     = SKColor.Parse("#4A5070");
    private static readonly SKColor AccentBlue    = SKColor.Parse("#4F8EF7");
    private static readonly SKColor AccentBlueDim = SKColor.Parse("#2A4A8A");
    private static readonly SKColor AgentPurple   = SKColor.Parse("#9B6DFF");
    private static readonly SKColor AgentDim      = SKColor.Parse("#4A2D8A");
    private static readonly SKColor BubbleOut     = SKColor.Parse("#1E3A6E");
    private static readonly SKColor BubbleIn      = SKColor.Parse("#161C2E");
    private static readonly SKColor OnlineGreen   = SKColor.Parse("#3DD68C");
    private static readonly SKColor AwayAmber     = SKColor.Parse("#F5A623");

    // ── Events ──
    public event Action<BoardColumn>? ColumnActivated;
    public event Action<BoardColumn, Message>? MessageSelected;
    public event Action<Message, BoardColumn>? MessageDroppedOnSpine;
    public event Action<Message, Message, BoardColumn>? MessageDroppedOnMessage;
    public event Action<Message, BoardColumn>? MessageDroppedOnComposer;
    public event Action? NavigateLeftRequested;
    public event Action? NavigateRightRequested;

    // ── Hit test rectangles (built during paint, used for mouse handling) ──
    private readonly List<(RectangleF Rect, BoardColumn Col)> _spineRects = new();
    private readonly List<(RectangleF Rect, Message Msg, BoardColumn Col)> _messageRects = new();
    private readonly List<(RectangleF Rect, BoardColumn Col)> _composerRects = new();
    private RectangleF _leftTriangleRect;
    private RectangleF _rightTriangleRect;

    public BoardSurface(VelocityService velocity)
    {
        _velocity = velocity;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(8, 12, 22);

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
        _animTimer.Tick += (_, _) => { AnimateWidths(); UpdateComposerOverlay(); ApplyEdgeScroll(); Invalidate(); };
        _animTimer.Start();

        AllowDrop = true;
        InitComposerControls();
    }

    public void SetColumns(List<BoardColumn> columns)
    {
        _columns = columns;
        Invalidate();
    }

    public void SetAnimSpeed(float speed) => _animSpeed = speed;

    // ── Composer overlay ────────────────────────────────────────────────────────

    private void InitComposerControls()
    {
        _composerTextBox = new TextBox
        {
            Multiline = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(28, 36, 56),
            ForeColor = Color.FromArgb(232, 234, 240),
            Font = new Font("Segoe UI", 10f),
            Visible = false,
            TabStop = true
        };
        _composerTextBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendComposerMessage(); }
        };
        _composerTextBox.GotFocus += (_, _) =>
        {
            var col = _columns.FirstOrDefault(c => c.IsActive);
            if (col != null) col.IsComposerActive = true;
            Invalidate();
        };
        _composerTextBox.LostFocus += (_, _) =>
        {
            var col = _columns.FirstOrDefault(c => c.IsActive);
            if (col != null) col.IsComposerActive = false;
            Invalidate();
        };

        _sendButton = new Button
        {
            Text = "▲",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(79, 142, 247),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Visible = false,
            TabStop = false
        };
        _sendButton.FlatAppearance.BorderSize = 0;
        _sendButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 162, 255);
        _sendButton.Click += (_, _) => SendComposerMessage();
        _sendButton.SizeChanged += (_, _) =>
        {
            if (_sendButton.Width > 0 && _sendButton.Height > 0)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddEllipse(0, 0, _sendButton.Width - 1, _sendButton.Height - 1);
                _sendButton.Region = new Region(path);
            }
        };

        Controls.Add(_composerTextBox);
        Controls.Add(_sendButton);
    }

    private void UpdateComposerOverlay()
    {
        var activeCol = _columns.OrderBy(c => c.SortIndex).FirstOrDefault(c => c.IsActive);
        if (activeCol == null)
        {
            if (_composerTextBox.Visible) _composerTextBox.Visible = false;
            if (_sendButton.Visible) _sendButton.Visible = false;
            return;
        }

        bool columnChanged = activeCol != _lastActiveCol;
        if (columnChanged)
        {
            if (_lastActiveCol != null) _lastActiveCol.DraftText = _composerTextBox.Text;
            _composerTextBox.Text = activeCol.DraftText;
            _composerTextBox.SelectionStart = _composerTextBox.Text.Length;
            _composerTextBox.PlaceholderText = $"Message {activeCol.Contact.Name.Split(' ')[0]}…";
            _lastActiveCol = activeCol;
            _pendingComposerFocus = true;
        }

        // Active column's x in Skia (device pixel) coordinates, accounting for horizontal scroll
        float skiaCx = -_horizontalScrollOffset;
        foreach (var col in _columns.OrderBy(c => c.SortIndex))
        {
            if (col == activeCol) break;
            skiaCx += col.CurrentWidth + 2f;
        }
        float skiaCw = activeCol.CurrentWidth;
        float skiaCh = Height / _skiaToLogicalY; // device-pixel canvas height
        float skiaY = skiaCh - ComposerHeight;

        if (skiaCw < 70f) { _composerTextBox.Visible = false; _sendButton.Visible = false; return; }

        // ── Send button ──
        // Skia circle: center (x+w-24, y+40), r=16 → bounding box left=x+w-40, top=y+24, 32×32
        float btnSkiaX = skiaCx + skiaCw - 40f;
        float btnSkiaY = skiaY + 24f;
        int logBtnLeft = (int)(btnSkiaX * _skiaToLogicalX);
        int logBtnTop  = (int)(btnSkiaY * _skiaToLogicalY);
        int logBtnSize = Math.Max(4, (int)(32f * _skiaToLogicalX));

        var skCol = activeCol.Contact.Color;
        var btnColor = Color.FromArgb(skCol.Alpha, skCol.Red, skCol.Green, skCol.Blue);
        if (_sendButton.BackColor != btnColor)
            _sendButton.BackColor = btnColor;

        _sendButton.SetBounds(logBtnLeft, logBtnTop, logBtnSize, logBtnSize);
        _sendButton.Visible = true;

        // ── TextBox: right edge tied to Skia input rect right (x+w-76), independent of button ──
        // Single-line TextBox ignores height — center it vertically in the Skia input rect
        float inputSkiaY = skiaY + 20f;
        float inputSkiaH = 38f;
        int logTbLeft  = (int)((skiaCx + 10f) * _skiaToLogicalX) + 10;
        int logTbRight = (int)((skiaCx + skiaCw - 76f) * _skiaToLogicalX);
        int logTbWidth = logTbRight - logTbLeft;
        int logRectTop = (int)(inputSkiaY * _skiaToLogicalY);
        int logRectH   = (int)(inputSkiaH * _skiaToLogicalY);
        int logTbTop   = logRectTop + (logRectH - _composerTextBox.Height) / 2;

        if (logTbWidth > 0)
        {
            _composerTextBox.SetBounds(logTbLeft, logTbTop, logTbWidth, _composerTextBox.Height);
            _composerTextBox.Visible = true;
            if (_pendingComposerFocus) { _composerTextBox.Focus(); _pendingComposerFocus = false; }
        }
        else
        {
            _composerTextBox.Visible = false;
        }
    }

    private void SendComposerMessage()
    {
        var text = _composerTextBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var activeCol = _columns.FirstOrDefault(c => c.IsActive);
        if (activeCol == null) return;

        var thread = activeCol.ActiveThread;
        if (thread == null)
        {
            thread = new ConversationThread { Thid = Guid.NewGuid().ToString() };
            activeCol.Contact.Threads.Add(thread);
            activeCol.ActiveThread = thread;
        }

        thread.Messages.Add(new Message
        {
            Id = Guid.NewGuid().ToString(),
            Thid = thread.Thid,
            Content = text,
            SenderDid = "did:drn:svrn7.net:michael#pandochat",
            IsOutbound = true,
            Timestamp = DateTime.UtcNow
        });

        activeCol.SelectedMessage = thread.Messages[^1];
        activeCol.ScrollOffset = float.MaxValue;
        activeCol.DraftText = string.Empty;
        _composerTextBox.Clear();
        _composerTextBox.Focus();
        Invalidate();
    }

    // ── Edge scroll ────────────────────────────────────────────────────────────

    private void ApplyEdgeScroll()
    {
        float canvasW = Width  / _skiaToLogicalX;
        float canvasH = Height / _skiaToLogicalY;
        float mx = _currentMousePos.X / _skiaToLogicalX;
        float my = _currentMousePos.Y / _skiaToLogicalY;

        // ── Horizontal edge scroll ──
        float totalColsW = _columns.Sum(c => c.CurrentWidth) + Math.Max(0, _columns.Count - 1) * 2f;
        float maxHScroll = Math.Max(0f, totalColsW - canvasW);
        if (maxHScroll > 0f)
        {
            const float HZone     = 80f;
            const float HMaxSpeed = 14f;
            float hDelta = 0f;
            if (mx > canvasW - HZone)
                hDelta =  HMaxSpeed * (1f - (canvasW - mx) / HZone);
            else if (mx < HZone)
                hDelta = -HMaxSpeed * (1f - mx / HZone);
            if (hDelta != 0f)
                _horizontalScrollOffset = Math.Clamp(_horizontalScrollOffset + hDelta, 0f, maxHScroll);
        }

        // ── Vertical edge scroll (active column message area) ──
        var activeCol = _columns.FirstOrDefault(c => c.IsActive);
        if (activeCol == null) return;

        float colX = -_horizontalScrollOffset;
        foreach (var col in _columns.OrderBy(c => c.SortIndex))
        {
            if (col == activeCol) break;
            colX += col.CurrentWidth + 2f;
        }
        float colW = activeCol.CurrentWidth;

        if (mx < colX || mx > colX + colW) return;
        float msgTop    = HeaderHeight;
        float msgBottom = canvasH - ComposerHeight;
        if (my < msgTop || my > msgBottom) return;

        const float Zone     = 60f;
        const float MaxSpeed = 10f;
        float delta = 0f;
        float distTop = my - msgTop;
        if (distTop < Zone)
            delta = -MaxSpeed * (1f - distTop / Zone);
        float distBottom = msgBottom - my;
        if (distBottom < Zone)
            delta = MaxSpeed * (1f - distBottom / Zone);
        if (delta != 0f)
            activeCol.ScrollOffset += delta;
    }

    // ── Animation ──────────────────────────────────────────────────────────────

    private void AnimateWidths()
    {
        bool anyMoving = false;
        foreach (var col in _columns)
        {
            float target = col.TargetWidth;
            float current = col.CurrentWidth;
            float diff = target - current;
            if (Math.Abs(diff) < 0.5f)
            {
                col.CurrentWidth = target;
            }
            else
            {
                // Fast expand, slightly slower collapse
                float factor = diff > 0 ? _animSpeed * 1.3f : _animSpeed;
                col.CurrentWidth += diff * factor;
                anyMoving = true;
            }
        }
    }

    // ── Paint ──────────────────────────────────────────────────────────────────

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        if (Width > 0) _skiaToLogicalX = (float)Width / e.Info.Width;
        if (Height > 0) _skiaToLogicalY = (float)Height / e.Info.Height;

        var canvas = e.Surface.Canvas;
        canvas.Clear(BgColor);

        _spineRects.Clear();
        _messageRects.Clear();
        _composerRects.Clear();

        float totalColsW = _columns.Sum(c => c.CurrentWidth) + Math.Max(0, _columns.Count - 1) * 2f;
        float maxHScroll = Math.Max(0f, totalColsW - e.Info.Width);
        _horizontalScrollOffset = Math.Clamp(_horizontalScrollOffset, 0f, maxHScroll);

        float x = -_horizontalScrollOffset;
        float h = e.Info.Height;

        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            float w = col.CurrentWidth;

            if (col.IsActive)
                DrawActiveColumn(canvas, col, x, 0, w, h);
            else
                DrawSpine(canvas, col, x, 0, w, h);

            x += w + 2; // 2px gap between columns
        }

        DrawNavigationTriangles(canvas, e.Info.Width, e.Info.Height);
        DrawDragOverlay(canvas);
    }

    // ── Spine ─────────────────────────────────────────────────────────────────

    private void DrawSpine(SKCanvas canvas, BoardColumn col, float x, float y, float w, float h)
    {
        var contact = col.Contact;
        var color = contact.Color;
        var rect = new SKRect(x, y, x + w, y + h);

        // Background
        using var bgPaint = new SKPaint { Color = color.WithAlpha(25), IsAntialias = true };
        canvas.DrawRect(rect, bgPaint);

        // Left accent bar
        using var accentPaint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y, x + 3, y + h), accentPaint);

        // Drop highlight
        if (_dropTargetColumn == col && _isDragging && _dropTargetMessage == null && !_dropOnComposer)
        {
            using var dropPaint = new SKPaint { Color = color.WithAlpha(60), IsAntialias = true };
            canvas.DrawRect(rect, dropPaint);
        }

        // Unread pip
        int unread = contact.Threads.Sum(t => t.UnreadCount);
        if (unread > 0)
        {
            using var pipPaint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawCircle(x + w / 2, y + 14, 4, pipPaint);
        }

        // Contact name — rotated vertically, bottom to top
        using var namePaint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Typeface = _sansBold,
            TextSize = 11f
        };

        float nameWidth = namePaint.MeasureText(contact.Name);
        canvas.Save();
        canvas.Translate(x + w / 2 + 4, y + 24 + nameWidth / 2);
        canvas.RotateDegrees(-90);
        canvas.DrawText(contact.Name, -nameWidth / 2, 0, namePaint);
        canvas.Restore();

        // Status dot
        var dotColor = contact.Status switch
        {
            ContactStatus.Online => OnlineGreen,
            ContactStatus.Away => AwayAmber,
            _ => TextMuted
        };
        using var dotPaint = new SKPaint { Color = dotColor, IsAntialias = true };
        canvas.DrawCircle(x + w / 2, y + h - 12, 4, dotPaint);
        using var dotBorder = new SKPaint { Color = BgColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        canvas.DrawCircle(x + w / 2, y + h - 12, 4, dotBorder);

        _spineRects.Add((new RectangleF(x, y, w, h), col));
    }

    // ── Active Column ──────────────────────────────────────────────────────────

    private void DrawActiveColumn(SKCanvas canvas, BoardColumn col, float x, float y, float w, float h)
    {
        var contact = col.Contact;
        var color = contact.Color;

        // Background
        using var bgPaint = new SKPaint { Color = BgColor, IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y, x + w, y + h), bgPaint);

        // ── Header ──
        DrawColumnHeader(canvas, col, x, y, w);

        // ── Messages ──
        float msgAreaTop = y + HeaderHeight;
        float msgAreaBottom = h - ComposerHeight;
        DrawMessageArea(canvas, col, x, msgAreaTop, w, msgAreaBottom);

        // ── Composer ──
        DrawComposer(canvas, col, x, h - ComposerHeight, w);

        // Left border accent — drawn last so header/composer backgrounds don't cover it
        using var accentPaint = new SKPaint { Color = color.WithAlpha(120), IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y, x + 3, y + h), accentPaint);
    }

    private void DrawColumnHeader(SKCanvas canvas, BoardColumn col, float x, float y, float w)
    {
        var contact = col.Contact;
        var color = contact.Color;

        // Header background
        using var hdrBg = new SKPaint { Color = Surface2.WithAlpha(180), IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y, x + w, y + HeaderHeight), hdrBg);

        // Avatar circle — solid fill matching the Send button, white initials
        float avX = x + 14, avY = y + 17, avR = 18;
        using var avBg = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawCircle(avX + avR, avY + avR, avR, avBg);

        using var initPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Typeface = _sansBold, TextSize = contact.IsMyAgent ? 16f : 12f, TextAlign = SKTextAlign.Center };
        canvas.DrawText(contact.Initials, avX + avR, avY + avR + 5, initPaint);

        // Name
        using var namePaint = new SKPaint { Color = color, IsAntialias = true, Typeface = _sansBold, TextSize = 14f };
        canvas.DrawText(contact.Name, x + 50, y + 22, namePaint);

        // Status
        var statusColor = contact.Status switch { ContactStatus.Online => OnlineGreen, ContactStatus.Away => AwayAmber, _ => TextMuted };
        using var statusDot = new SKPaint { Color = statusColor, IsAntialias = true };
        canvas.DrawCircle(x + w - 16, y + 20, 4, statusDot);

        // Header bottom border
        using var borderPaint = new SKPaint { Color = Surface3, IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y + HeaderHeight - 1, x + w, y + HeaderHeight), borderPaint);
    }

    private void DrawMessageArea(SKCanvas canvas, BoardColumn col, float x, float top, float w, float bottom)
    {
        if (col.ActiveThread == null) return;

        var messages = col.ActiveThread.Messages;
        float bubbleW = w * 0.78f; // same width for both directions
        float lineH = 18f;
        float visibleH = bottom - top;

        // Pre-pass: compute total content height so we can clamp scroll
        float totalH = MessagePadding;
        foreach (var msg in messages)
        {
            bool exp = col.SelectedMessage == msg || col.HoveredMessage == msg;
            using var p = new SKPaint { Typeface = _sans, TextSize = 13f, IsAntialias = true };
            var ls = WrapText(msg.Content, p, bubbleW - 24);
            int vis = exp ? ls.Count : Math.Min(2, ls.Count);
            float bH = vis * lineH + 28;
            if (exp) bH = Math.Min(bH, visibleH * (col.SelectedMessage == msg ? 0.5f : 0.35f));
            totalH += bH + 4;
        }

        float maxScroll = Math.Max(0f, totalH - visibleH);
        col.ScrollOffset = Math.Clamp(col.ScrollOffset, 0f, maxScroll);

        float yPos = top + MessagePadding - col.ScrollOffset;

        canvas.Save();
        canvas.ClipRect(new SKRect(x, top, x + w, bottom));

        foreach (var msg in messages)
        {
            bool isSelected = col.SelectedMessage == msg;
            bool isHovered = col.HoveredMessage == msg;
            bool expand = isSelected || isHovered;

            using var contentPaint = new SKPaint { Typeface = _sans, TextSize = 13f, IsAntialias = true };
            var lines = WrapText(msg.Content, contentPaint, bubbleW - 24);
            int visibleLines = expand ? lines.Count : Math.Min(2, lines.Count);
            float bubbleH = visibleLines * lineH + 28;

            if (expand)
            {
                float cap = visibleH * (isSelected ? 0.5f : 0.35f);
                bubbleH = Math.Min(bubbleH, cap);
            }

            if (yPos + bubbleH > top) // at least partially visible
            {
                float bubbleX = msg.IsOutbound ? x + w - bubbleW - 8 : x + 8;
                var bubbleRect = new SKRectI((int)bubbleX, (int)yPos, (int)(bubbleX + bubbleW), (int)(yPos + bubbleH));
                bool isDropTarget = _dropTargetMessage == msg && _isDragging;
                DrawMessageBubble(canvas, col, msg, bubbleRect, visibleLines, lines, lineH, isDropTarget, isHovered, isSelected);
                _messageRects.Add((new RectangleF(bubbleRect.Left, bubbleRect.Top, bubbleRect.Width, bubbleRect.Height), msg, col));
            }

            yPos += bubbleH + 4;
            if (yPos > bottom) break;
        }

        // Scroll thumb
        if (maxScroll > 0)
        {
            float thumbH = Math.Max(24f, visibleH * visibleH / totalH);
            float thumbY = top + col.ScrollOffset / maxScroll * (visibleH - thumbH);
            using var thumbPaint = new SKPaint { Color = TextMuted.WithAlpha(90), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(x + w - 5, thumbY, x + w - 2, thumbY + thumbH), 2f, 2f, thumbPaint);
        }

        canvas.Restore();
    }

    private void DrawMessageBubble(SKCanvas canvas, BoardColumn col, Message msg,
        SKRectI rect, int visibleLines, List<string> lines, float lineH, bool isDropTarget,
        bool isHovered = false, bool isSelected = false)
    {
        var contactColor = col.Contact.Color;
        var bgColor = msg.IsOutbound ? BubbleOut : (col.Contact.IsMyAgent ? SKColor.Parse("#1E1040") : BubbleIn);

        SKColor borderColor;
        float strokeWidth;
        if (isSelected)
        {
            bgColor = new SKColor(
                (byte)Math.Min(bgColor.Red + 18, 255),
                (byte)Math.Min(bgColor.Green + 18, 255),
                (byte)Math.Min(bgColor.Blue + 18, 255));
            borderColor = contactColor;
            strokeWidth = 2f;
        }
        else if (isHovered)
        {
            bgColor = new SKColor(
                (byte)Math.Min(bgColor.Red + 10, 255),
                (byte)Math.Min(bgColor.Green + 10, 255),
                (byte)Math.Min(bgColor.Blue + 10, 255));
            borderColor = contactColor.WithAlpha(160);
            strokeWidth = 1.5f;
        }
        else
        {
            borderColor = msg.IsOutbound ? AccentBlueDim : (col.Contact.IsMyAgent ? AgentDim : Surface3);
            strokeWidth = 1f;
        }

        if (isDropTarget) bgColor = bgColor.WithAlpha(200);

        // Bubble background
        using var bubblePaint = new SKPaint { Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(rect, BubbleRadius, BubbleRadius, bubblePaint);

        // Border
        using var borderPaint = new SKPaint { Color = borderColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth };
        canvas.DrawRoundRect(rect, BubbleRadius, BubbleRadius, borderPaint);

        // Selection stripe on the near edge (right for outbound, left for inbound)
        if (isSelected)
        {
            using var stripePaint = new SKPaint { Color = contactColor, IsAntialias = true };
            if (msg.IsOutbound)
                canvas.DrawRoundRect(new SKRect(rect.Right - 4, rect.Top + 5, rect.Right - 1, rect.Bottom - 5), 1.5f, 1.5f, stripePaint);
            else
                canvas.DrawRoundRect(new SKRect(rect.Left + 1, rect.Top + 5, rect.Left + 4, rect.Bottom - 5), 1.5f, 1.5f, stripePaint);
        }

        float textX = rect.Left + 10;
        float textY = rect.Top + 16;

        // DIDComm @type label for agent
        if (!string.IsNullOrEmpty(msg.DIDCommType))
        {
            using var typePaint = new SKPaint { Color = AgentPurple.WithAlpha(180), IsAntialias = true, Typeface = _mono, TextSize = 8f };
            string typeLabel = msg.DIDCommType.Length > 55 ? msg.DIDCommType[..52] + "…" : msg.DIDCommType;
            canvas.DrawText(typeLabel, textX, textY, typePaint);
            textY += 13;
        }

        // Content lines
        using var textPaint = new SKPaint { Color = TextPrimary, IsAntialias = true, Typeface = _sans, TextSize = 13f };
        for (int i = 0; i < visibleLines && i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], textX, textY, textPaint);
            textY += lineH;
        }

        // "▾ N more" when message is truncated
        if (visibleLines < lines.Count)
        {
            int hidden = lines.Count - visibleLines;
            using var morePaint = new SKPaint { Color = contactColor.WithAlpha(180), IsAntialias = true, Typeface = _sans, TextSize = 9f };
            canvas.DrawText($"▾ {hidden} more", rect.Right - 62, rect.Bottom - 6, morePaint);
        }

        // Footer: timestamp
        using var tsPaint = new SKPaint { Color = TextMuted, IsAntialias = true, Typeface = _sans, TextSize = 9f };
        string ts = msg.Timestamp.ToLocalTime().ToString("h:mm tt");
        if (msg.IsVerified) ts += " · ✓ verified";
        canvas.DrawText(ts, textX, rect.Bottom - 6, tsPaint);
    }

    private void DrawComposer(SKCanvas canvas, BoardColumn col, float x, float y, float w)
    {
        var color = col.Contact.Color;
        var borderColor = col.IsComposerActive ? color : Surface3;

        // Background
        using var bgPaint = new SKPaint { Color = Surface2, IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y, x + w, y + ComposerHeight), bgPaint);

        // Top border
        using var topBorder = new SKPaint { Color = Surface3, IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y, x + w, y + 1), topBorder);

        // Input box
        var inputRect = new SKRect(x + 10, y + 20, x + w - 76, y + 58);
        using var inputBg = new SKPaint { Color = Surface3, IsAntialias = true };
        canvas.DrawRoundRect(inputRect, 20, 20, inputBg);
        using var inputBorder = new SKPaint { Color = borderColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        canvas.DrawRoundRect(inputRect, 20, 20, inputBorder);

        // Send button
        float btnX = x + w - 24, btnY = y + 24, btnR = 16;
        using var btnPaint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawCircle(btnX, btnY + btnR, btnR, btnPaint);
        using var arrowPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Typeface = _sans, TextSize = 14f, TextAlign = SKTextAlign.Center };
        canvas.DrawText("▲", btnX, btnY + btnR + 5, arrowPaint);

        // Drop target on composer
        if (_dropOnComposer && _dropTargetColumn == col && _isDragging)
        {
            using var dropPaint = new SKPaint { Color = color.WithAlpha(40), IsAntialias = true };
            canvas.DrawRect(new SKRect(x, y, x + w, y + ComposerHeight), dropPaint);
        }

        _composerRects.Add((new RectangleF(x, y, w, ComposerHeight), col));
    }

    private void DrawNavigationTriangles(SKCanvas canvas, int totalWidth, int totalHeight)
    {
        var activeCol = _columns.FirstOrDefault(c => c.IsActive);
        if (activeCol == null) return;

        bool showLeft = activeCol.SortIndex > 0;
        bool showRight = activeCol.SortIndex < _columns.Count - 1;

        float triY = totalHeight / 2f;

        if (showLeft)
        {
            using var paint = new SKPaint { Color = TextDim.WithAlpha(130), IsAntialias = true, Typeface = _sans, TextSize = 20f, TextAlign = SKTextAlign.Center };
            canvas.DrawText("◀", 16, triY + 7, paint);
            _leftTriangleRect = new RectangleF(0, triY - 16, 32, 32);
        }

        if (showRight)
        {
            using var paint = new SKPaint { Color = TextDim.WithAlpha(130), IsAntialias = true, Typeface = _sans, TextSize = 20f, TextAlign = SKTextAlign.Center };
            canvas.DrawText("▶", totalWidth - 16, triY + 7, paint);
            _rightTriangleRect = new RectangleF(totalWidth - 32, triY - 16, 32, 32);
        }
    }

    private void DrawDragOverlay(SKCanvas canvas)
    {
        if (!_isDragging || _draggingMessage == null) return;

        // Draw a small floating label near the cursor to indicate dragging
        Point cursor = PointToClient(Cursor.Position);
        using var dragPaint = new SKPaint { Color = AccentBlue.WithAlpha(200), IsAntialias = true };
        canvas.DrawRoundRect(cursor.X + 10, cursor.Y - 10, 180, 24, 6, 6, dragPaint);
        using var dragText = new SKPaint { Color = SKColors.White, IsAntialias = true, Typeface = _sans, TextSize = 11f };
        string preview = _draggingMessage.ContentPreview;
        if (preview.Length > 28) preview = preview[..25] + "…";
        canvas.DrawText(preview, cursor.X + 16, cursor.Y + 6, dragText);
    }

    // ── Mouse Events ───────────────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _currentMousePos = e.Location;

        if (_isDragging)
        {
            UpdateDragTarget(e.Location);
            Invalidate();
            return;
        }

        // Hover over spine → activate column
        foreach (var (rect, col) in _spineRects)
        {
            if (rect.Contains(e.X, e.Y) && !col.IsActive)
            {
                SetActiveColumn(col);
                return;
            }
        }

        // Hover over message in active column → hover-expand (no selection)
        var activeCol = _columns.FirstOrDefault(c => c.IsActive);
        if (activeCol != null)
        {
            Message? hoveredMsg = null;
            foreach (var (rect, msg, col) in _messageRects)
            {
                if (col == activeCol && rect.Contains(e.X, e.Y))
                {
                    hoveredMsg = msg;
                    break;
                }
            }

            if (activeCol.HoveredMessage != hoveredMsg)
            {
                activeCol.HoveredMessage = hoveredMsg;
                Invalidate();
            }
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        // Triangle navigation
        if (_leftTriangleRect.Contains(e.X, e.Y)) { NavigateLeftRequested?.Invoke(); return; }
        if (_rightTriangleRect.Contains(e.X, e.Y)) { NavigateRightRequested?.Invoke(); return; }

        // Click on spine → activate
        foreach (var (rect, col) in _spineRects)
            if (rect.Contains(e.X, e.Y)) { SetActiveColumn(col); return; }

        // Click on message → select (transfer selection)
        foreach (var (rect, msg, col) in _messageRects)
        {
            if (rect.Contains(e.X, e.Y))
            {
                col.SelectedMessage = msg;
                col.HoveredMessage = null;
                MessageSelected?.Invoke(col, msg);
                Invalidate();
                return;
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragStartPoint = e.Location;

            foreach (var (rect, msg, col) in _messageRects)
            {
                if (rect.Contains(e.X, e.Y))
                {
                    _draggingMessage = msg;
                    break;
                }
            }
        }
    }

    // Override to detect drag start
    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        const int WM_MOUSEMOVE = 0x0200;
        if (m.Msg == WM_MOUSEMOVE && _draggingMessage != null && !_isDragging)
        {
            var pos = PointToClient(Cursor.Position);
            if (Math.Abs(pos.X - _dragStartPoint.X) > 4 || Math.Abs(pos.Y - _dragStartPoint.Y) > 4)
                _isDragging = true;
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isDragging && _draggingMessage != null)
        {
            if (_dropOnComposer && _dropTargetColumn != null)
                MessageDroppedOnComposer?.Invoke(_draggingMessage, _dropTargetColumn);
            else if (_dropTargetMessage != null && _dropTargetColumn != null)
                MessageDroppedOnMessage?.Invoke(_draggingMessage, _dropTargetMessage, _dropTargetColumn);
            else if (_dropTargetColumn != null)
                MessageDroppedOnSpine?.Invoke(_draggingMessage, _dropTargetColumn);
        }

        _isDragging = false;
        _draggingMessage = null;
        _dropTargetColumn = null;
        _dropTargetMessage = null;
        _dropOnComposer = false;
        Invalidate();
    }

    private void UpdateDragTarget(Point p)
    {
        _dropTargetColumn = null;
        _dropTargetMessage = null;
        _dropOnComposer = false;

        foreach (var (rect, col) in _composerRects)
            if (rect.Contains(p.X, p.Y)) { _dropTargetColumn = col; _dropOnComposer = true; return; }

        foreach (var (rect, msg, col) in _messageRects)
            if (rect.Contains(p.X, p.Y)) { _dropTargetMessage = msg; _dropTargetColumn = col; return; }

        foreach (var (rect, col) in _spineRects)
            if (rect.Contains(p.X, p.Y)) { _dropTargetColumn = col; return; }
    }

    // ── Keyboard ───────────────────────────────────────────────────────────────

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (!_composerTextBox.Focused && !_composerTextBox.Visible) Focus(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var activeCol = _columns.FirstOrDefault(c => c.IsActive);
        if (activeCol == null) return;
        activeCol.ScrollOffset -= e.Delta / 120f * 52f; // wheel up → negative delta → scroll toward top
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) NavigateLeftRequested?.Invoke();
        if (e.KeyCode == Keys.Right) NavigateRightRequested?.Invoke();
    }

    // ── Column activation ──────────────────────────────────────────────────────

    public void SetActiveColumn(BoardColumn col)
    {
        foreach (var c in _columns)
        {
            c.IsActive = false;
            c.TargetWidth = SpineWidth;
        }
        col.IsActive = true;
        col.TargetWidth = ActiveWidth;
        col.ScrollOffset = float.MaxValue; // scroll to newest messages
        ColumnActivated?.Invoke(col);
        Invalidate();
    }

    public void NavigateLeft()
    {
        var active = _columns.FirstOrDefault(c => c.IsActive);
        if (active == null || active.SortIndex == 0) return;
        SetActiveColumn(_columns[active.SortIndex - 1]);
    }

    public void NavigateRight()
    {
        var active = _columns.FirstOrDefault(c => c.IsActive);
        if (active == null || active.SortIndex == _columns.Count - 1) return;
        SetActiveColumn(_columns[active.SortIndex + 1]);
    }

    // ── Text wrapping ──────────────────────────────────────────────────────────

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(' ');
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            string test = current.Length == 0 ? word : current + " " + word;
            if (paint.MeasureText(test) > maxWidth && current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
            else
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }
}
