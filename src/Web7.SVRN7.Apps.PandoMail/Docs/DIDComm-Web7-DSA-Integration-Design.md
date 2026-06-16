# DIDComm / Web 7.0 DSA Integration Design for OLAF

## Conceptual Mapping

| Outlook/SMTP concept | DIDComm / Web 7.0 DSA equivalent |
|---|---|
| Email address (`alice@example.com`) | DID (`did:drn:alpha.svrn7.net:citizen:alice`) |
| Mail server | DIDComm service endpoint in DID Document |
| IMAP inbox | DSA Mediator agent (routed DIDComm messages) |
| Message body (HTML) | DIDComm `basicmessage` or custom `dsa/email` plaintext |
| TLS transport security | JWE envelope encryption (X25519 / ECDH-ES+A256KW) |
| DKIM signature | JWS detached signature (Ed25519) |
| Email thread | DIDComm `thid` (thread ID) |
| Attachment | DIDComm attachment with `data.links[]` or CID hash |
| Address book contact | Resolved DID Document |
| Online/offline indicator | Mediator routing status |

---

## Layer Stack

```
┌────────────────────────────────────────────┐
│  OLAF WinForms UI (unchanged visually)     │
│  MainForm, LeftSpine, MessageArea,         │
│  RightSpine, MessageList, FolderView       │
├────────────────────────────────────────────┤
│  DIDComm Mail Adapter Layer (NEW)          │
│  DIDCommMailAdapter  ←→  MailMessage       │
│  MessageStore (extended)                   │
├────────────────────────────────────────────┤
│  DIDComm Protocol Layer (NEW)              │
│  DIDCommMessageFactory  (pack/unpack)      │
│  DIDCommClient          (HTTP transport)   │
│  DIDResolver            (DID → DIDDoc)     │
│  KeyStore               (private keys)     │
├────────────────────────────────────────────┤
│  Web 7.0 DSA Agent (external)              │
│  Mediator endpoint  /receive               │
│  Pickup protocol    /inbox                 │
│  DID Document       /.well-known/did.json  │
└────────────────────────────────────────────┘
```

---

## New File Structure

```
OLAF/
├── DIDComm/
│   ├── DIDCommClient.cs         — HTTP POST to service endpoints
│   ├── DIDCommMessageFactory.cs — Pack (sign+encrypt) / Unpack (verify+decrypt)
│   ├── DIDResolver.cs           — did:drn resolver + cache
│   ├── KeyStore.cs              — Ed25519/X25519 key pairs, persisted locally
│   ├── DIDDocument.cs           — DID Document data model
│   └── DIDCommMessage.cs        — Plaintext message model (type, from, to, body, attachments)
├── MailServer/
│   ├── MailMessage.cs           — EXTENDED: +SenderDID, +ThreadID, +Verified, +Encrypted
│   └── MessageStore.cs          — EXTENDED: DIDComm inbox poll loop
```

---

## Core Data Models

### `DIDCommMessage.cs`

```csharp
namespace System.Windows.Forms.Samples.DIDComm
{
    // DIDComm v2 plaintext message structure
    public class DIDCommMessage
    {
        public string Id { get; set; }       // UUID
        public string Type { get; set; }     // message type URI
        public string From { get; set; }     // sender DID
        public string[] To { get; set; }     // recipient DIDs
        public string Thid { get; set; }     // thread ID
        public long CreatedTime { get; set; }// Unix epoch ms
        public DIDCommBody Body { get; set; }
        public DIDCommAttachment[] Attachments { get; set; }
    }

    public class DIDCommBody
    {
        // For type: "https://web7.dsa/email/1.0/message"
        public string Subject { get; set; }
        public string Content { get; set; }  // HTML or plaintext
        public string ContentType { get; set; } // "text/html"
    }

    public class DIDCommAttachment
    {
        public string Id { get; set; }
        public string MediaType { get; set; }
        public DIDCommAttachmentData Data { get; set; }
    }

    public class DIDCommAttachmentData
    {
        public string[] Links { get; set; }  // URL to fetch
        public string Base64 { get; set; }   // inline base64
        public string Hash { get; set; }     // SHA-256 integrity
    }
}
```

### `DIDDocument.cs`

```csharp
public class DIDDocument
{
    public string Id { get; set; }           // the DID
    public VerificationMethod[] VerificationMethod { get; set; }
    public string[] Authentication { get; set; }
    public string[] KeyAgreement { get; set; }
    public ServiceEndpoint[] Service { get; set; }
}

public class VerificationMethod
{
    public string Id { get; set; }
    public string Type { get; set; }         // "JsonWebKey2020"
    public string Controller { get; set; }
    public JObject PublicKeyJwk { get; set; }
}

public class ServiceEndpoint
{
    public string Id { get; set; }
    public string Type { get; set; }         // "DIDCommMessaging"
    public string ServiceEndpoint { get; set; } // HTTPS URL
    public string[] Accept { get; set; }     // ["didcomm/v2"]
    public string[] RoutingKeys { get; set; }
}
```

---

## Extended `MailMessage.cs`

The existing `MailMessage` gains DIDComm fields while preserving all existing `INotifyPropertyChanged` bindings:

```csharp
// Existing fields unchanged: From, To, Cc, Subject, Read, SentDate, Path

// NEW fields:
private string _senderDid;
public string SenderDID
{
    get { return _senderDid; }
    set { _senderDid = value; OnPropertyChanged("SenderDID"); }
}

private string _threadId;
public string ThreadID
{
    get { return _threadId; }
    set { _threadId = value; OnPropertyChanged("ThreadID"); }
}

private bool _verified;
public bool Verified      // Ed25519 signature verified
{
    get { return _verified; }
    set { _verified = value; OnPropertyChanged("Verified"); }
}

private bool _encrypted;
public bool Encrypted     // Was JWE-encrypted in transit
{
    get { return _encrypted; }
    set { _encrypted = value; OnPropertyChanged("Encrypted"); }
}

// DID-aware display name: resolves "did:drn:..." → human name if available
public string DisplayFrom => string.IsNullOrEmpty(SenderDID) ? From
    : $"{From} <{SenderDID}>";
```

---

## `DIDResolver.cs`

```csharp
public class DIDResolver
{
    private readonly Dictionary<string, DIDDocument> _cache
        = new Dictionary<string, DIDDocument>();

    // did:drn:alpha.svrn7.net:citizen:alice
    // → https://alpha.svrn7.net/citizen/alice/did.json
    public DIDDocument Resolve(string did)
    {
        if (_cache.ContainsKey(did)) return _cache[did];

        string url = DIDDrnToUrl(did);
        string json = FetchJson(url);
        var doc = JsonConvert.DeserializeObject<DIDDocument>(json);
        _cache[did] = doc;
        return doc;
    }

    private string DIDDrnToUrl(string did)
    {
        // did:drn:alpha.svrn7.net:citizen:alice → https://alpha.svrn7.net/citizen/alice/did.json
        var parts = did.Split(':');
        // parts[0]="did", parts[1]="drn", parts[2]=host, parts[3..]=path
        string host = Uri.UnescapeDataString(parts[2]);
        string path = parts.Length > 3
            ? string.Join("/", parts, 3, parts.Length - 3)
            : ".well-known";
        return $"https://{host}/{path}/did.json";
    }

    private string FetchJson(string url)
    {
        using (var client = new System.Net.WebClient())
            return client.DownloadString(url);
    }
}
```

---

## `KeyStore.cs`

```csharp
public class KeyStore
{
    // Stored in %APPDATA%\OLAF\keys.json (encrypted with DPAPI)
    private static readonly string KeyPath =
        Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "OLAF", "keys.json");

    public string UserDID { get; private set; }

    // X25519 key pair for ECDH-ES encryption (JWE)
    public byte[] X25519PrivateKey { get; private set; }
    public byte[] X25519PublicKey { get; private set; }

    // Ed25519 key pair for signing (JWS)
    public byte[] Ed25519PrivateKey { get; private set; }
    public byte[] Ed25519PublicKey { get; private set; }

    public void LoadOrGenerate()
    {
        if (File.Exists(KeyPath))
            LoadFromDisk();
        else
            GenerateAndSave();
    }

    // Uses Windows DPAPI: ProtectedData.Protect/Unprotect
    // for at-rest key encryption
}
```

---

## `DIDCommClient.cs` — Transport

```csharp
public class DIDCommClient
{
    private readonly DIDResolver _resolver;
    private readonly string _mediatorUrl; // Web 7.0 DSA agent endpoint

    // SEND: pack message → POST to recipient's service endpoint
    public void Send(DIDCommMessage message, string factory_packedJwe)
    {
        var doc = _resolver.Resolve(message.To[0]);
        var endpoint = GetDIDCommEndpoint(doc);

        using (var client = new System.Net.WebClient())
        {
            client.Headers[System.Net.HttpRequestHeader.ContentType]
                = "application/didcomm-encrypted+json";
            client.UploadString(endpoint.ServiceEndpoint, factory_packedJwe);
        }
    }

    // RECEIVE: poll mediator for queued messages (pickup protocol v2)
    // POST {"type":"https://didcomm.org/messagepickup/3.0/status-request"}
    // then: {"type":"https://didcomm.org/messagepickup/3.0/delivery-request"}
    public IEnumerable<string> PollInbox()
    {
        // Returns list of packed JWE strings
        // MessageStore calls this on a BackgroundWorker timer
    }

    private ServiceEndpoint GetDIDCommEndpoint(DIDDocument doc)
        => doc.Service.First(s => s.Type == "DIDCommMessaging");
}
```

---

## `MessageStore.cs` — Extended Inbox Polling

The existing singleton gains a background poll loop that replaces static XML:

```csharp
// NEW members alongside existing ones:
private DIDCommClient _didcommClient;
private DIDCommMessageFactory _factory;
private DIDCommMailAdapter _adapter;
private System.Threading.Timer _pollTimer;

public void StartDIDCommInbox(string mediatorUrl)
{
    _didcommClient = new DIDCommClient(_resolver, mediatorUrl);
    _factory = new DIDCommMessageFactory(KeyStore.Instance);
    _adapter = new DIDCommMailAdapter();

    // Poll every 30 seconds (same cadence as Outlook's Send/Receive)
    _pollTimer = new System.Threading.Timer(PollCallback, null,
        TimeSpan.Zero, TimeSpan.FromSeconds(30));
}

private void PollCallback(object state)
{
    foreach (string packedJwe in _didcommClient.PollInbox())
    {
        DIDCommMessage msg = _factory.Unpack(packedJwe); // decrypt + verify
        MailMessage mail = _adapter.ToMailMessage(msg);
        // Marshal to UI thread — same pattern as NetworkAvailabilityChanged
        if (_form.InvokeRequired)
            _form.Invoke(new Action(() => AddMessage(mail)));
        else
            AddMessage(mail);
    }
}

private void AddMessage(MailMessage mail)
{
    _messages.Add(mail);
    UnreadCount++;
    OnPropertyChanged("UnreadCount");
}
```

---

## `DIDCommMailAdapter.cs` — Protocol ↔ Model Bridge

```csharp
public class DIDCommMailAdapter
{
    public MailMessage ToMailMessage(DIDCommMessage msg)
    {
        return new MailMessage
        {
            From        = ExtractDisplayName(msg.From),
            To          = string.Join("; ", msg.To),
            Subject     = msg.Body?.Subject ?? "(no subject)",
            SentDate    = DateTimeOffset
                              .FromUnixTimeMilliseconds(msg.CreatedTime)
                              .LocalDateTime,
            SenderDID   = msg.From,
            ThreadID    = msg.Thid ?? msg.Id,
            Verified    = true,   // set by DIDCommMessageFactory.Unpack
            Encrypted   = true,   // it was a JWE
            Read        = false,
            Path        = null,   // body inline, not in embedded resource
            HtmlBody    = msg.Body?.Content   // NEW field, see RightSpine change
        };
    }

    public DIDCommMessage FromMailMessage(MailMessage mail,
        string senderDid, string recipientDid)
    {
        return new DIDCommMessage
        {
            Id          = Guid.NewGuid().ToString(),
            Type        = "https://web7.dsa/email/1.0/message",
            From        = senderDid,
            To          = new[] { recipientDid },
            Thid        = mail.ThreadID ?? Guid.NewGuid().ToString(),
            CreatedTime = new DateTimeOffset(mail.SentDate).ToUnixTimeMilliseconds(),
            Body        = new DIDCommBody
            {
                Subject     = mail.Subject,
                Content     = mail.HtmlBody,
                ContentType = "text/html"
            }
        };
    }

    private string ExtractDisplayName(string did)
    {
        // did:drn:alpha.svrn7.net:citizen:alice → alice@alpha.svrn7.net
        var parts = did.Split(':');
        if (parts.Length >= 3 && parts[1] == "drn")
        {
            string host = parts[2];
            string user = parts.Length > 3 ? parts[parts.Length - 1] : "user";
            return $"{user}@{host}";
        }
        return did; // fallback: show raw DID
    }
}
```

---

## `RightSpine.cs` — DID Verification Badge

The reading pane gains a verification status line above the existing header labels:

```csharp
// In RightSpine.Message setter (existing code loads HTML into WebBrowser):
// ADD before webBrowser1.DocumentStream = ...:

if (value.SenderDID != null)
{
    labelVerified.Text = value.Verified
        ? $"Verified: {value.SenderDID}"
        : $"UNVERIFIED - {value.SenderDID}";
    labelVerified.ForeColor = value.Verified
        ? Color.DarkGreen : Color.DarkRed;
    labelVerified.Visible = true;
}
else
{
    labelVerified.Visible = false;
}

// For inline HTML body (DIDComm messages, no .htm embedded resource):
if (!string.IsNullOrEmpty(value.HtmlBody))
{
    webBrowser1.DocumentText = value.HtmlBody;
}
else if (!string.IsNullOrEmpty(value.Path))
{
    // existing embedded resource load path — unchanged
    var doc = assembly.GetManifestResourceStream(value.Path);
    webBrowser1.DocumentStream = doc;
}
```

---

## Message Flow: Receiving

```
Web 7.0 DSA Mediator
        │  (JWE packed, routed to user's DID)
        ▼
DIDCommClient.PollInbox()
        │  packed JWE string
        ▼
DIDCommMessageFactory.Unpack()
        │  1. JWE decrypt with X25519 private key
        │  2. JWS verify signature against sender's DID Document
        │  → DIDCommMessage (plaintext, Verified=true)
        ▼
DIDCommMailAdapter.ToMailMessage()
        │  → MailMessage (SenderDID, ThreadID, Verified, HtmlBody)
        ▼
MessageStore.AddMessage()   [UI thread via Invoke]
        │  fires PropertyChanged("UnreadCount")
        ▼
FolderView repaints Inbox count
MessageList shows new row
RightSpine shows message + DID badge
```

## Message Flow: Sending (Compose)

```
User fills compose form
        │  Subject, Body (HTML), recipient DID
        ▼
DIDCommMailAdapter.FromMailMessage()
        │  → DIDCommMessage
        ▼
DIDResolver.Resolve(recipientDID)
        │  fetches DID Document → service endpoint + X25519 public key
        ▼
DIDCommMessageFactory.Pack()
        │  1. JWS sign with sender Ed25519 private key
        │  2. JWE encrypt with recipient X25519 public key
        │  → packed JWE string
        ▼
DIDCommClient.Send()
        │  POST application/didcomm-encrypted+json
        │  → recipient's DIDComm service endpoint
        ▼
MessageStore adds to Sent folder
```

---

## Web 7.0 DSA Agent Configuration

```json
{
  "id": "did:drn:alpha.svrn7.net:citizen:mwherman",
  "verificationMethod": [
    {
      "id": "#key-1",
      "type": "JsonWebKey2020",
      "publicKeyJwk": { "kty": "OKP", "crv": "Ed25519", "x": "..." }
    },
    {
      "id": "#key-2",
      "type": "JsonWebKey2020",
      "publicKeyJwk": { "kty": "OKP", "crv": "X25519", "x": "..." }
    }
  ],
  "authentication": ["#key-1"],
  "keyAgreement": ["#key-2"],
  "service": [
    {
      "id": "#didcomm",
      "type": "DIDCommMessaging",
      "serviceEndpoint": "https://mediator.web7.dsa/receive",
      "accept": ["didcomm/v2"],
      "routingKeys": ["did:drn:alpha.svrn7.net:mediator#key-1"]
    }
  ]
}
```

---

## What Stays Unchanged

- All rendering code (`BaseStackStrip`, `HeaderStrip`, `StackStrip`, gradients)
- `FolderView` painting (`DrawAnnotatedText`, `[n]` unread counts)
- `MessageList` owner-drawn DataGridView cells
- `SortableBindingList<MailMessage>` / `PropertyComparer<T>` sorting
- `INotifyPropertyChanged` binding chain throughout
- `SystemEvents.UserPreferenceChanged` font chain
- All existing `MailMessage` properties and their bindings

The DIDComm layer slots in purely at the **data source** layer (`MessageStore` constructor + new poll loop) and the **data model** layer (additive fields on `MailMessage`). The UI is completely transport-agnostic.

---

## NuGet Dependencies Needed

| Package | Purpose |
|---|---|
| `Newtonsoft.Json` | DID Document + DIDComm JSON parsing |
| `NSec.Cryptography` | Ed25519 sign/verify, X25519 ECDH |
| `jose-jwt` | JWE encrypt/decrypt, JWS sign/verify |
| `System.Security.Cryptography` | DPAPI for KeyStore at-rest protection |

All others remain zero-dependency as today.

---

## Menu Item Implementation Status

All menu items defined in `MainForm.Designer.cs` are **unimplemented stubs**. No `Click` event handlers are wired up in the Designer, and `MainForm.cs` contains only three methods unrelated to menu actions:

- `Form1_Load` — initializes the form
- `Form1_UserPreferenceChanged` — handles system theme/preference changes
- `UpdateStatusBar` — updates the connection status display

The application is a UI shell. The table below lists every menu item for implementation planning.

| Menu | Item | Variable Name | Shortcut |
|---|---|---|---|
| **File** | Ne&w (submenu) | `newToolStripMenuItem` | |
| | &Mail Message | `mailMessageToolStripMenuItem` | Ctrl+N |
| | &Post in This Folder | `postinThisFolderToolStripMenuItem` | Ctrl+Shift+S |
| | Fold&er... | `folderToolStripMenuItem1` | Ctrl+Shift+E |
| | &Search Folder... | `searchFolderToolStripMenuItem` | Ctrl+Shift+P |
| | Na&vigation Pane Shortcut... | `navigationPaneShortcutToolStripMenuItem` | |
| | &Appointment | `appointmentToolStripMenuItem` | Ctrl+Shift+A |
| | Meeting Re&quest | `meetingRequestToolStripMenuItem` | Ctrl+Shift+Q |
| | &Contact | `contactToolStripMenuItem` | Ctrl+Shift+C |
| | Distribution &List | `distributionListToolStripMenuItem` | Ctrl+Shift+L |
| | &Task | `taskToolStripMenuItem` | Ctrl+Shift+K |
| | Task &Request | `taskRequestToolStripMenuItem` | Ctrl+Shift+U |
| | &Journal Entry | `journalEntryToolStripMenuItem` | Ctrl+Shift+J |
| | &Note | `noteToolStripMenuItem` | Ctrl+Shift+U |
| | Internet Fa&x | `internetFaxToolStripMenuItem` | Ctrl+Shift+X |
| | Ch&oose Form... | `chooseFormToolStripMenuItem` | |
| | Outlook Data &File... | `outlookDataFileToolStripMenuItem` | |
| | &Open (submenu) | `openToolStripMenuItem` | |
| | Open Items... | `openItemsToolStripMenuItem` | |
| | Clos&e All Items | `closeAllItemsToolStripMenuItem` | |
| | Save &As... | `saveAsToolStripMenuItem` | |
| | Save Attachments | `saveAttachmentsToolStripMenuItem` | |
| | &Folder | `folderToolStripMenuItem` | |
| | &Data File Management... | `dataFileManagementToolStripMenuItem` | |
| | Impor&t and Export... | `importandExportToolStripMenuItem` | |
| | A&rchive... | `archiveToolStripMenuItem` | |
| | Page Set&up (submenu) | `pageSetupToolStripMenuItem` | |
| | Print Pre&view | `printPreviewToolStripMenuItem` | |
| | &Print | `printToolStripMenuItem` | Ctrl+P |
| | Wor&k Offline | `toolStripMenuItem1` | |
| | E&xit | `toolStripMenuItem7` | |
| **Edit** | &Undo | `undoToolStripMenuItem` | Ctrl+Z |
| | Cu&t | `cutToolStripMenuItem` | Ctrl+X |
| | &Copy | `copyToolStripMenuItem` | Ctrl+C |
| | &Paste | `pasteToolStripMenuItem` | Ctrl+V |
| | Select A&ll | `selectAllToolStripMenuItem` | Ctrl+A |
| | &Delete | `deleteToolStripMenuItem` | Ctrl+D |
| | &Move to Folder | `movetoFolderToolStripMenuItem` | Ctrl+Shift+V |
| | Cop&y to Folder... | `copytoFolderToolStripMenuItem` | |
| | Mar&k as Read | `markasReadToolStripMenuItem` | Ctrl+Q |
| | Mark as U&nread | `markasUnreadToolStripMenuItem` | Ctrl+U |
| | Mark All as R&ead | `markAllasReadToolStripMenuItem` | |
| | Categories... | `categoriesToolStripMenuItem` | |
| **View** | &Arrange By | `arrangeByToolStripMenuItem` | |
| | Na&vigation Pane | `navigationPaneToolStripMenuItem` | |
| | Reading Pa&ne | `readingPaneToolStripMenuItem` | |
| | Auto&Preview | `autoPreviewToolStripMenuItem` | |
| | E&xpand/Collapse Groups | `expandCollapseGroupsToolStripMenuItem` | |
| | Reminder W&indow | `reminderWindowToolStripMenuItem` | |
| | &Refresh | `refreshToolStripMenuItem` | |
| | &Status Bar | `statusBarToolStripMenuItem` | |
| **Go** | &Mail | `mailToolStripMenuItem` | |
| | &Calendar | `calendarToolStripMenuItem` | |
| | Cont&acts | `contactsToolStripMenuItem` | |
| | &Notifications | `tasksToolStripMenuItem` | |
| | &Notes | `notesToolStripMenuItem` | |
| | Folder &List | `folderListToolStripMenuItem` | |
| | Shortc&uts | `shortcutsToolStripMenuItem` | |
| | &Jornal | `jornalToolStripMenuItem` | |
| | &Folder | `folderToolStripMenuItem2` | |
| **Tools** | S&end/Receive | `sendReceiveToolStripMenuItem` | |
| | F&ind | `findToolStripMenuItem` | |
| | Address &Book... | `addressBookToolStripMenuItem` | |
| | Organi&ze | `organizeToolStripMenuItem` | |
| | Ru&les and Alerts... | `rulesandSlertsToolStripMenuItem` | |
| | O&ut of Office Assistant... | `outofOfficeAssistantToolStripMenuItem` | |
| | Mailbo&x Cleanup... | `mailboxCleanupToolStripMenuItem` | |
| | Empt&y "Deleted Items" Folder | `emptyDeletedItemsFolderToolStripMenuItem` | |
| | Recover Dele&ted Items... | `recoverDeletedItemsToolStripMenuItem` | |
| | E-mail &Accounts... | `emailAccountsToolStripMenuItem` | |
| | &Customize... | `customizeToolStripMenuItem` | |
| | Options... | `optionsToolStripMenuItem2` | |
| **Actions** | &New Mail Message | `newMailMessageToolStripMenuItem` | Ctrl+N |
| | &Reply | `replyToolStripMenuItem` | |
| | Reply to A&ll | `replytoAllToolStripMenuItem` | |
| | For&ward | `forwardToolStripMenuItem` | |
| **Help** | Microsoft Office Outlook &Help | `microsoftOfficeoutlookHelpToolStripMenuItem` | |
| | &Microsoft Office Online | `microsoftOfficeOnlineToolStripMenuItem` | |
| | &Contact Us | `contactUsToolStripMenuItem` | |
| | Chec&k for Updates | `checkforUpdatesToolStripMenuItem` | |
| | Detect and &Repair... | `detectandRepairToolStripMenuItem` | |
| | &About Microsoft Office Outlook | `aboutMicrosoftOfficeOutlookToolStripMenuItem` | |
