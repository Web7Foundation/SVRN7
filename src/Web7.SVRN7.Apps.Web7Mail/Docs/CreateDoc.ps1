$docPath = 'C:\SVRN7\repos\Outlook 2003 Look and Feel\Docs\OLAF Design Document.docx'

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $word.Documents.Add()
$sel = $word.Selection

function H1($text) {
    $sel.Style = $doc.Styles('Heading 1')
    $sel.TypeText($text)
    $sel.TypeParagraph()
}
function H2($text) {
    $sel.Style = $doc.Styles('Heading 2')
    $sel.TypeText($text)
    $sel.TypeParagraph()
}
function Para($text) {
    $sel.Style = $doc.Styles('Normal')
    $sel.TypeText($text)
    $sel.TypeParagraph()
}
function Bullet($text) {
    $sel.Style = $doc.Styles('List Bullet')
    $sel.TypeText($text)
    $sel.TypeParagraph()
}

# Title
$sel.Style = $doc.Styles('Title')
$sel.TypeText('OLAF — Outlook 2003 Look and Feel')
$sel.TypeParagraph()

Para 'Design Document'
Para 'Project: C:\SVRN7\repos\Outlook 2003 Look and Feel\C#'
Para 'Namespace: System.Windows.Forms.Samples'
Para 'Target Framework: .NET Framework 4.0 (WinForms)'
$sel.TypeParagraph()

# Overview
H1 'Overview'
Para 'OLAF is a .NET Framework WinForms demonstration application that reproduces the Microsoft Outlook 2003 user interface using entirely custom-drawn controls. It was built as a showcase of advanced WinForms techniques including owner drawing, ToolStrip customization, data binding, and system-font awareness.'

# Entry Point
H1 'Entry Point'
Para 'Program.cs — Standard WinForms entry point. Enables visual styles and launches MainForm.'

# Main Window
H1 'Main Window'
Para 'MainForm.cs — The top-level Form. On load it:'
Bullet 'Initializes the MessageStore singleton and displays message count in the status bar.'
Bullet 'Loads online/offline status images (Outlook.bmp / Error.bmp) from embedded resources.'
Bullet 'Subscribes to NetworkChange.NetworkAvailabilityChanged to update the connection status label in real time.'
Bullet 'Subscribes to SystemEvents.UserPreferenceChanged to reflow fonts when the user changes system appearance settings.'

# Data Layer
H1 'Data Layer (MailServer/)'

H2 'MailMessage.cs'
Para 'A simple model class representing a single email. Implements INotifyPropertyChanged. Fields:'
Bullet 'From, To, Cc — sender and recipient strings'
Bullet 'Subject — message subject line'
Bullet 'Read — boolean read/unread flag'
Bullet 'SentDate — DateTime the message was sent'
Bullet 'Path — filename of the embedded HTML body resource'

H2 'MessageStore.cs'
Para 'A singleton data store (accessed via GetMessageStore()). Responsibilities:'
Bullet 'Loads message records from an embedded XML resource (Mail/Inbox.xml) into a SortableBindingList<MailMessage> on first access.'
Bullet 'Tracks SelectedMessage, UnreadCount, DraftsCount, and DeletedCount.'
Bullet 'Fires INotifyPropertyChanged notifications so all bound controls stay in sync.'
Bullet 'Contains SortableBindingList<T> — a BindingList<T> subclass that supports column sorting via ApplySortCore.'
Bullet 'Contains PropertyComparer<T> — a generic IComparer<T> that compares any property by name using reflection, supporting ascending and descending sort directions.'

# Custom Controls
H1 'Custom Controls (Custom Controls/)'

H2 'BaseStackStrip.cs'
Para 'Inherits ToolStrip. Base class for the left-panel navigation strips. Key behaviours:'
Bullet 'Forces DockStyle.Fill, hides the grip, disables overflow and auto-size.'
Bullet 'Installs a custom ToolStripProfessionalRenderer with RoundedEdges = false.'
Bullet 'Paints a vertical linear gradient background using the renderer colour table (ToolStripGradientMiddle to ToolStripGradientEnd) and draws a dark border.'
Bullet 'Exposes OnSetRenderer and OnSetFonts virtual methods for subclasses.'
Bullet 'Responds to SystemEvents.UserPreferenceChanged to reset fonts.'

H2 'StackStrip.cs'
Para 'Inherits BaseStackStrip. The vertical list of navigation buttons (Mail, Calendar, Contacts, Tasks, etc.):'
Bullet 'Uses ToolStripLayoutStyle.VerticalStackWithOverflow.'
Bullet 'Renders each ToolStripButton with a gradient fill (normal / hover / pressed states) and a dark outline border.'
Bullet 'Enforces radio-button behaviour: exactly one button is always checked; unchecking the last checked button re-checks it.'
Bullet 'Raises an ItemHeightChanged event when the font changes, so LeftSpine can recalculate splitter distances.'

H2 'HeaderStrip.cs'
Para 'Inherits ToolStrip. A gradient header bar used above each content pane. Supports two styles via the HeaderStyle property:'
Bullet 'Large — bold Arial, white foreground, deep-blue gradient (OverflowButtonGradientMiddle to End).'
Bullet 'Small — system menu font, black foreground, light grey gradient (MenuStripGradientEnd to Begin).'
Para 'Height is auto-calculated from the font metrics plus 6px padding.'

H2 'LeftSpine.cs'
Para 'A UserControl that composes the entire left navigation pane. Layout:'
Bullet 'A SplitContainer (stackStripSplitter) divides the pane into the folder/content area (top) and the StackStrip (bottom).'
Bullet 'Below the StackStrip sits an overflow BaseStackStrip (overflowStrip) showing small icon-only buttons for navigation items that are scrolled off screen.'
Bullet 'On load, overflow buttons are created in reverse order from the StackStrip items using bitmap resources looked up by tag name.'
Bullet 'The splitter is snapped to item-height increments; dragging it shows/hides overflow icons dynamically.'
Bullet 'Sets parent Padding = (3, 3, 0, 3) for consistent inset margins.'

H2 'FolderView.cs'
Para 'A UserControl wrapping a TreeView that shows the Outlook folder hierarchy (Inbox, Drafts, Deleted Items, Sent Items, etc.). Key features:'
Bullet 'Owner-drawn nodes (TreeViewDrawMode.OwnerDrawText): Inbox shows unread count in green brackets, Drafts and Deleted Items show counts in blue parentheses. Nodes with non-zero counts are rendered in bold.'
Bullet 'Subscribes to MessageStore.PropertyChanged to update counts live.'
Bullet 'Uses an ImageList with 25 named bitmaps for folder icons.'
Bullet 'Bold/normal node fonts are set per-node via tag convention (Tag = "Bold").'

H2 'MessageList.cs'
Para 'A UserControl containing a DataGridView bound to MessageStore.Messages via a BindingSource. Three columns:'
Bullet 'Column 0 (image, 25 px) — read (envelope-open) or unread (envelope-closed) icon, painted via CellValueNeeded in virtual mode.'
Bullet 'Column 1 (fill width) — custom-painted merged cell: sender name in bold on the first line, subject in grey on the second line. Unread messages use bold for both lines.'
Bullet 'Column 2 (105 px, right-aligned) — sent date; shows time-of-day format for today messages, day+date otherwise.'
Para 'Row borders are drawn manually in RowPostPaint: a focus rectangle for the selected row, a light-grey rectangle for all others. When the binding-source position changes, MessageStore.SelectedMessage is updated.'

H2 'MessageArea.cs'
Para 'A thin partial UserControl used as a layout container for the message reading area. Sets DockStyle.Fill and applies parent padding (0, 3, 0, 3) on load.'

H2 'RightSpine.cs'
Para 'A UserControl that displays the full reading pane for the selected email. Layout:'
Bullet 'A top Panel (panel2) shows subject (bold Arial 12), sender name (Arial 11), a replied/not-responded banner, and To/Cc fields in a TableLayoutPanel.'
Bullet 'A WebBrowser below panel2 fills the remaining space and renders the embedded HTML body (Mail/<path>.htm).'
Bullet 'Subscribes to MessageStore.PropertyChanged; when SelectedMessage changes it updates all header labels and loads the new HTML stream from the assembly manifest resources.'
Bullet 'Paints a diagonal gradient background (dark slate to lighter slate) behind the border panel in OnPaint.'
Bullet 'Sets parent Padding = (0, 3, 3, 3).'

# Embedded Resources
H1 'Embedded Resources'
Para 'All content is compiled into the assembly as embedded resources:'
Bullet 'Mail/Inbox.xml — XML dataset defining all inbox messages (fields: From, To, CC, Subject, SentDate, Read, Path).'
Bullet 'Mail/*.htm — Individual HTML message bodies, one per sender: Brian, Chris, Erick, Harsh, Hender, Iain, Jamie, Jeff, Laurie, Mark, Other, Rideout, Shawn, Simon, Steve, VoiceMail.'
Bullet 'Images/*.bmp / .gif / .png — Toolbar icons, folder icons, and read/unread envelope images.'
Bullet 'Properties/Resources.resx — Typed resource wrapper giving strongly-typed access to bitmaps (e.g. Properties.Resources.Outlook, Properties.Resources.Read, Properties.Resources.Unread).'

# Key Design Patterns
H1 'Key Design Patterns'

H2 'Singleton — MessageStore'
Para 'MessageStore.GetMessageStore() returns the single shared instance. All controls obtain data through this one object, ensuring counts and selection stay consistent across the entire UI without explicit cross-control coupling.'

H2 'INotifyPropertyChanged + BindingSource'
Para 'MailMessage and MessageStore both implement INotifyPropertyChanged. MessageList binds its DataGridView to MessageStore.Messages via a BindingSource. FolderView and RightSpine subscribe directly to MessageStore.PropertyChanged for lightweight updates (count badges, message pane refresh).'

H2 'Custom ToolStrip Rendering'
Para 'Rather than replacing the renderer entirely, each strip installs a ToolStripProfessionalRenderer and hooks only the RenderToolStripBackground and (for StackStrip) RenderButtonBackground events. Gradient colours are read from the renderer''s own ProfessionalColorTable, so the app honours the current Windows visual theme.'

H2 'Owner Drawing'
Para 'Three controls perform owner drawing:'
Bullet 'FolderView — TreeViewDrawMode.OwnerDrawText for annotated folder names.'
Bullet 'MessageList — DataGridView CellPainting for the merged From+Subject cell and image column.'
Bullet 'RightSpine — OnPaint override for the background gradient.'

H2 'System Font / DPI Awareness'
Para 'Every control subscribes to SystemEvents.UserPreferenceChanged and resets fonts from SystemFonts.IconTitleFont or SystemFonts.MenuFont. MainForm calls PerformAutoScale() after updating its Font property, keeping layout correct across DPI changes or accessibility-font changes at runtime.'

# Build
H1 'Build'
Para 'The project uses the old MSBuild 2003 .csproj format (non-SDK style), targeting .NET Framework 4.0. It must be built with the Visual Studio MSBuild toolchain, not the .NET SDK CLI:'
Para '"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" OLAF.sln /p:Configuration=Debug'
Para 'Output: OLAF\bin\Debug\OLAF.exe'

# Save
$doc.SaveAs2($docPath, 16)
$doc.Close()
$word.Quit()

Write-Output "Saved: $docPath"
