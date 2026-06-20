# Web 7.0 Pando Mail — Web 7.0 Pando Mail App

A C# Windows Forms sample application that faithfully recreates the Microsoft Outlook 2003 user interface using standard .NET Windows Forms controls. Originally authored by Microsoft (© 2004) as a demonstration of advanced WinForms rendering and layout techniques.

## Overview

Web 7.0 Pando Mail shows how to achieve the classic Outlook 2003 three-pane layout — navigation sidebar, message list, and reading pane — entirely with `ToolStrip`, `DataGridView`, `TreeView`, and custom owner-drawn controls. No third-party UI libraries are required.

```
┌─────────────────────────────────────────────────────────────┐
│  HeaderStrip (large gradient title bar)                     │
├──────────────┬──────────────────────┬───────────────────────┤
│              │  MessageList         │                       │
│  FolderView  │  (DataGridView,      │   MessageArea         │
│  (TreeView   │   owner-drawn rows,  │   (reading pane,      │
│   w/ folder  │   read/unread icons) │    HTML body via      │
│   counts)    │                      │    WebBrowser)        │
├──────────────┴──────────────────────┴───────────────────────┤
│  StackStrip (Mail / Calendar / Contacts / Tasks buttons)    │
│  OverflowStrip (icon-only overflow items)                   │
├─────────────────────────────────────────────────────────────┤
│  StatusBar — "All Folders are up to date." / Connected      │
└─────────────────────────────────────────────────────────────┘
```

## Features

- **Outlook 2003 visual fidelity** — gradient toolbars, professional color table rendering, square-edged strips, grip dots on the splitter bar
- **Resizable navigation sidebar** — `LeftSpine` uses a `SplitContainer` whose splitter snaps to button-height increments; overflow items automatically appear/disappear as the panel is resized
- **Live folder counts** — `FolderView` renders Inbox unread count in green, Drafts and Deleted item counts in blue, using owner-drawn `TreeView` nodes that update in real time via `INotifyPropertyChanged`
- **Multi-line message rows** — `MessageList` custom-paints `DataGridView` cells to show sender on the first line and subject on the second, with unread messages in bold
- **Read/unread state tracking** — selecting a message in `MessageList` automatically marks it read and decrements the unread counter in the `MessageStore` and `FolderView`
- **Sortable message list** — backed by `SortableBindingList<T>` with a generic `PropertyComparer<T>`, sorted newest-first on startup
- **Network connectivity detection** — status bar updates live when the network comes or goes via `NetworkChange.NetworkAvailabilityChanged`
- **System font awareness** — all controls respond to `UserPreferenceChanged` and re-scale when the user changes the Windows icon title font

## Project Structure

```
Web 7.0 Pando Mail/
├── Web 7.0 Pando Mail.sln
└── Web 7.0 Pando Mail/
    ├── Program.cs                   # Entry point
    ├── MainForm.cs / .Designer.cs   # Application shell, status bar, network monitoring
    ├── Custom Controls/
    │   ├── BaseStackStrip.cs        # ToolStrip base — gradient background, professional renderer
    │   ├── StackStrip.cs            # Navigation button bar (radio-button semantics)
    │   ├── HeaderStrip.cs           # Title bar (Large = bold white on blue, Small = black)
    │   ├── LeftSpine.cs / .resx     # Left panel: FolderView + StackStrip + overflow
    │   ├── FolderView.cs / .resx    # Mail folder TreeView with live counts
    │   ├── MessageList.cs / .resx   # Email list DataGridView (owner-drawn)
    │   ├── MessageArea.cs / .resx   # Reading pane
    │   └── RightSpine.cs / .resx   # Right panel container
    ├── MailServer/
    │   ├── MailMessage.cs           # Email model (INotifyPropertyChanged)
    │   └── MessageStore.cs          # Singleton data store; loads Inbox.xml; SortableBindingList
    ├── Mail/
    │   ├── Inbox.xml                # Embedded XML message manifest
    │   └── *.htm                    # Individual email HTML bodies
    └── Properties/
        ├── AssemblyInfo.cs
        ├── Resources.Designer.cs    # Embedded bitmaps (Outlook icon, Read/Unread, toolbar images)
        └── Settings.Designer.cs
```

## Key Classes

| Class | Description |
|---|---|
| `MainForm` | Top-level form; wires up `MessageStore`, status bar, and network events |
| `BaseStackStrip` | Abstract `ToolStrip` subclass that installs a `ToolStripProfessionalRenderer` with custom gradient background painting |
| `StackStrip` | Extends `BaseStackStrip` with vertical layout and radio-button checked-state enforcement across `ToolStripButton` items |
| `HeaderStrip` | `ToolStrip` with `AreaHeaderStyle.Large` (bold white Arial on blue gradient) or `Small` (system font, dark text) |
| `LeftSpine` | `UserControl` combining `FolderView`, `StackStrip`, and an overflow strip in a `SplitContainer`; handles splitter snapping and overflow visibility |
| `FolderView` | Owner-drawn `TreeView` that annotates Inbox, Drafts, and Deleted Items nodes with live counts |
| `MessageList` | `DataGridView` in virtual mode; custom cell painting produces two-line rows with read/unread icon |
| `MessageArea` | Reading pane container |
| `MailMessage` | Plain data model — `From`, `To`, `Cc`, `Subject`, `Read`, `SentDate`, `Path`; implements `INotifyPropertyChanged` |
| `MessageStore` | Singleton; deserializes `Inbox.xml` from the embedded assembly resource; tracks `SelectedMessage`, `UnreadCount`, `DraftsCount`, `DeletedCount` |
| `SortableBindingList<T>` | `BindingList<T>` subclass that implements `ApplySortCore` via `PropertyComparer<T>` |
| `PropertyComparer<T>` | Generic `IComparer<T>` that sorts on any named property using reflection and `IComparable` |

## Requirements

- Windows (Win32)
- .NET Framework 4.0 or later
- Visual Studio 2005 or later (solution was originally created for VS 2005)

## Building and Running

1. Open `Web 7.0 Pando Mail.sln` in Visual Studio.
2. Build the solution (`Ctrl+Shift+B`).
3. Run (`F5`). The application launches directly into the Outlook-style inbox view.

No additional dependencies or NuGet packages are needed.

## Background

This project was produced by Microsoft around 2004–2005 as a showcase for the Windows Forms 2.0 feature set introduced in .NET 2.0 — specifically `ToolStrip`, `DataGridView`, `BindingSource`, virtual-mode grid painting, and `ToolStripProfessionalRenderer`. It lives in the namespace `System.Windows.Forms.Samples` and was distributed as part of MSDN sample collections demonstrating how to build professional-quality Office-style applications without relying on COM interop or third-party controls.
