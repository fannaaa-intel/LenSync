Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules

    ''' <summary>
    ''' Base for a tab inside a TabbedView (Settings, Add Service): title + description at the top left, action buttons
    ''' at the top right (Actions), and the page's own controls below (subclasses place them in LayoutContent).
    ''' Pages sit on the view's white card, so their background is white.
    ''' </summary>
    Public MustInherit Class ModulePage
        Inherits UserControl

        Private ReadOnly _title As String
        Private ReadOnly _description As String
        Protected ReadOnly Actions As New FlowLayoutPanel()
        Private _loaded As Boolean

        Protected Sub New(title As String, description As String)
            _title = title
            _description = description
            AutoScaleMode = AutoScaleMode.None
            BackColor = Theme.White
            DoubleBuffered = True
            SetStyle(ControlStyles.ResizeRedraw, True)
            Actions.FlowDirection = FlowDirection.RightToLeft
            Actions.WrapContents = False
            Actions.BackColor = Theme.White
            Actions.AutoScroll = False
            Controls.Add(Actions)
        End Sub

        ''' <summary>
        ''' Called every time the page is shown. Loads on the first visit; later visits reload only when
        ''' RefreshOnShow is True (pages whose data other tabs change, e.g. bookings that move to Billing).
        ''' </summary>
        Public Async Function EnsureLoadedAsync() As Task
            If _loaded AndAlso Not RefreshOnShow Then Return
            _loaded = True
            Await ReloadAsync()
        End Function

        Public MustOverride Function ReloadAsync() As Task

        Protected Overridable ReadOnly Property RefreshOnShow As Boolean
            Get
                Return False
            End Get
        End Property

        ''' <summary>Pages with a Save button return True while edits are not saved.</summary>
        Public Overridable ReadOnly Property HasUnsavedChanges As Boolean
            Get
                Return False
            End Get
        End Property

        ''' <summary>Throws away unsaved edits (used when the user chooses "Discard" before leaving).</summary>
        Public Overridable Sub DiscardChanges()
        End Sub

        ''' <summary>Top of the area below the header.</summary>
        Protected ReadOnly Property ContentTop As Integer
            Get
                Return Theme.TitleSmall.Height + Theme.Label.Height + Theme.Dpi(26)
            End Get
        End Property

        Protected Overrides Sub OnHandleCreated(e As EventArgs)
            MyBase.OnHandleCreated(e)
            PerformLayout()
        End Sub

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            ' Layout also fires while this base constructor runs (e.g. setting AutoScaleMode), before the subclass has
            ' created its controls. Wait until the page has a window; by then every constructor has finished.
            If Not IsHandleCreated Then Return
            Dim actionsH = Theme.Dpi(Theme.ButtonHeight)
            Dim actionsW = Math.Min(Width \ 2 + Theme.Dpi(80), Actions.Controls.Cast(Of Control)().Where(Function(c) c.Visible).Sum(Function(c) c.Width + c.Margin.Horizontal))
            Actions.Bounds = New Rectangle(Width - actionsW, 0, actionsW, actionsH)
            LayoutContent(New Rectangle(0, ContentTop, Width, Math.Max(0, Height - ContentTop)))
        End Sub

        Protected MustOverride Sub LayoutContent(area As Rectangle)

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            Dim textW = Math.Max(0, Width - Actions.Width - Theme.Dpi(16))
            TextRenderer.DrawText(g, _title, Theme.TitleSmall, New Rectangle(0, 0, textW, Theme.TitleSmall.Height), Theme.Ink,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
            TextRenderer.DrawText(g, _description, Theme.Label, New Rectangle(0, Theme.TitleSmall.Height + Theme.Dpi(2), textW, Theme.Label.Height + 2),
                                  Theme.Slate, TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
        End Sub

        ''' <summary>Adds a button to the header (first added = right-most).</summary>
        Protected Function AddAction(button As LsButton) As LsButton
            button.Margin = New Padding(Theme.Dpi(10), 0, 0, 0)
            Actions.Controls.Add(button)
            PerformLayout()
            Return button
        End Function

        ''' <summary>Runs <paramref name="work"/> off the UI thread; on failure logs it, shows a friendly error and returns Nothing.</summary>
        Protected Async Function RunAsync(Of T)(work As Func(Of T), context As String) As Task(Of T)
            UseWaitCursor = True
            Try
                Return Await Task.Run(work)
            Catch ex As Exception
                AppLog.Write(ex, context)
                LsDialog.Error(Db.FriendlyMessage(ex))
                Return Nothing
            Finally
                UseWaitCursor = False
            End Try
        End Function

        ''' <summary>Standard label above a field.</summary>
        Protected Function AddFieldLabel(text As String) As Label
            Dim lbl As New Label() With {.Text = text, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal,
                                         .TextAlign = ContentAlignment.BottomLeft, .AutoEllipsis = True, .BackColor = Theme.White}
            Controls.Add(lbl)
            Return lbl
        End Function

        ''' <summary>
        ''' Lays out a toolbar row: <paramref name="left"/> controls flow from the left edge (the first one, usually a
        ''' search box, takes the spare width between its min and max), <paramref name="right"/> buttons align right
        ''' (first = right-most). Hidden controls are skipped. Returns the bottom of the row.
        ''' </summary>
        Protected Function LayoutToolbar(area As Rectangle, left As Control(), right As Control(),
                                         Optional searchMin96 As Integer = 160, Optional searchMax96 As Integer = 320) As Integer
            Dim gap = Theme.Dpi(10)
            Dim rowH = Theme.Dpi(40)
            Dim y = area.Top
            Dim r = area.Right
            For Each b In right
                If Not b.Visible Then Continue For
                b.Location = New Point(r - b.Width, y + (rowH - b.Height) \ 2)
                r = b.Left - gap
            Next
            Dim fixedW = left.Skip(1).Where(Function(c) c.Visible).Sum(Function(c) c.Width + Theme.Dpi(16))
            Dim x = area.Left
            For i = 0 To left.Length - 1
                Dim c = left(i)
                If Not c.Visible Then Continue For
                If i = 0 Then
                    Dim w = Math.Max(Theme.Dpi(searchMin96), Math.Min(Theme.Dpi(searchMax96), r - x - fixedW - Theme.Dpi(16)))
                    c.Bounds = New Rectangle(x, y, w, rowH)
                Else
                    c.Location = New Point(x, y + (rowH - c.Height) \ 2)
                End If
                x = c.Right + Theme.Dpi(16)
            Next
            Return y + rowH
        End Function

    End Class

End Namespace
