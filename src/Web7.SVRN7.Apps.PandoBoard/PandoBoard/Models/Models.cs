using SkiaSharp;

namespace Pando.Board.Models;

public class Contact
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Did { get; set; } = string.Empty;
    public bool IsMyAgent { get; set; }
    public SKColor Color { get; set; } = SKColor.Parse("#4F8EF7");
    public ContactStatus Status { get; set; } = ContactStatus.Offline;
    public List<ConversationThread> Threads { get; set; } = new();
    public double VelocityScore { get; set; }
    public bool IsLocked { get; set; }
    public string Initials => IsMyAgent ? "★" :
        string.Concat(Name.Split(' ').Take(2).Select(w => w.Length > 0 ? w[0].ToString() : ""));
}

public enum ContactStatus { Online, Away, Offline }

public class ConversationThread
{
    public string Thid { get; set; } = Guid.NewGuid().ToString();
    public List<Message> Messages { get; set; } = new();
    public int UnreadCount { get; set; }
    public string Label => Messages.FirstOrDefault()?.ContentPreview ?? "New thread";
    public DateTime LastActivity => Messages.LastOrDefault()?.Timestamp ?? DateTime.MinValue;
    public Message? LastMessage => Messages.LastOrDefault();
}

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Thid { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentPreview => Content.Length > 60 ? Content[..57] + "…" : Content;
    public string SenderDid { get; set; } = string.Empty;
    public bool IsOutbound { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsVerified { get; set; }
    public string? DIDCommType { get; set; }
}

public class BoardColumn
{
    public Contact Contact { get; set; } = null!;
    public int SortIndex { get; set; }
    public float CurrentWidth { get; set; } = SpineWidth;
    public float TargetWidth { get; set; } = SpineWidth;
    public bool IsActive { get; set; }
    public bool IsHovered { get; set; }
    public bool IsComposerActive { get; set; }
    public ConversationThread? ActiveThread { get; set; }
    public Message? SelectedMessage { get; set; }
    public Message? HoveredMessage { get; set; }
    public float ScrollOffset { get; set; } = float.MaxValue; // clamped to bottom on first paint
    public string DraftText { get; set; } = string.Empty;

    public const float SpineWidth = 52f;
    public const float ActiveWidth = 420f;
    public const float HoverWidth = 380f;
}
