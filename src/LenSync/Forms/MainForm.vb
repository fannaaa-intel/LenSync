Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports LenSync.Core
Imports LenSync.Modules
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' The shell (full window): ink sidebar with the modules this user may open, a header with the module title and
''' a live clock, and a content area where module views slide in. Sign out returns to the login screen.
''' </summary>
Public Class MainForm
    Inherits Form
    Implements IModuleHost

    Private NotInheritable Class ModuleInfo
        Public Key As String
        Public Title As String
        Public Subtitle As String
        Public Icon As LineIcon
        Public Phase As Integer
        Public Description As String
    End Class

    ' Every module the app knows about, in sidebar order. Each is shown only if the role has its permission.
    Private Shared ReadOnly AllModules As ModuleInfo() = {
        New ModuleInfo With {.Key = Permissions.Dashboard, .Title = "Dashboard", .Subtitle = "Today at a glance", .Icon = LineIcon.Dashboard,
                             .Phase = 5, .Description = "Today's services and sales, a 7-day sales chart and the latest transactions."},
        New ModuleInfo With {.Key = Permissions.AddService, .Title = "Add Service", .Subtitle = "Bookings, billing and payment", .Icon = LineIcon.AddService,
                             .Phase = 4, .Description = "Create bookings and record downpayments, then bill finished shoots with add-ons and discounts, take Cash, GCash or Maya payments and print the acknowledgment receipt."},
        New ModuleInfo With {.Key = Permissions.Reports, .Title = "Reports", .Subtitle = "Sales reports with verification codes", .Icon = LineIcon.Reports,
                             .Phase = 6, .Description = "Daily sales, transactions, payment methods, staff and package reports with a printable proof footer."},
        New ModuleInfo With {.Key = Permissions.Settings, .Title = "Settings", .Subtitle = "Users, roles, pricing and business info", .Icon = LineIcon.Settings,
                             .Phase = 3, .Description = "User accounts, role permissions, packages, add-ons, business details, logo, signature and email setup."}
    }

    Private ReadOnly _sidebar As New SidebarPanel()
    Private ReadOnly _header As New HeaderPanel()
    Private ReadOnly _content As New Panel()
    Private ReadOnly _nav As New List(Of SidebarButton)
    Private ReadOnly _btnPassword As New SidebarButton("Change password", LineIcon.Key)
    Private ReadOnly _btnSignOut As New SidebarButton("Log out", LineIcon.SignOut)
    Private ReadOnly _slideTween As New Tween()
    Private ReadOnly _clock As New Timer() With {.Interval = 1000}
    Private _current As Control
    Private _currentKey As String

    ''' <summary>True when the user chose Sign out (Program shows the login again); False when the window was closed.</summary>
    Public Property SignedOut As Boolean

    Public Sub New()
        AutoScaleMode = AutoScaleMode.None
        Text = "LENSYNC"
        BackColor = Theme.Snow
        Font = Theme.Body
        StartPosition = FormStartPosition.CenterScreen
        Dim wa = Screen.PrimaryScreen.WorkingArea
        Size = New Size(Math.Min(Theme.Dpi(1280), wa.Width), Math.Min(Theme.Dpi(760), wa.Height))
        MinimumSize = New Size(Math.Min(Theme.Dpi(1024), wa.Width), Math.Min(Theme.Dpi(640), wa.Height))
        WindowState = FormWindowState.Maximized
        KeyPreview = True
        DoubleBuffered = True

        _content.BackColor = Theme.Snow
        Controls.Add(_content)
        Controls.Add(_header)
        Controls.Add(_sidebar)

        Dim user = Session.CurrentUser
        _sidebar.SetUser(user.FullName, user.RoleName)
        Try
            _sidebar.SetStudio(AppSettings.GetValue(AppSettings.BusinessName, "LENSYNC Photo Studio"))
        Catch ex As Exception
            AppLog.Write(ex, "Loading studio name")
        End Try

        For Each m In AllModules
            If Not Session.HasPermission(m.Key) Then Continue For
            Dim b As New SidebarButton(m.Title, m.Icon, m.Key)
            Dim key = m.Key
            AddHandler b.Click, Sub() ShowModule(key)
            _nav.Add(b)
            _sidebar.Controls.Add(b)
        Next
        _sidebar.Controls.Add(_btnPassword)
        _sidebar.Controls.Add(_btnSignOut)
        AddHandler _btnPassword.Click, AddressOf OnChangePassword
        AddHandler _btnSignOut.Click, AddressOf OnSignOut
        AddHandler _sidebar.Layout, Sub() LayoutSidebar()
        AddHandler _clock.Tick, Sub() _header.Tick()
        AddHandler AppEvents.StudioChanged, AddressOf OnStudioChanged
        AddHandler AppEvents.CurrentUserChanged, AddressOf OnCurrentUserChanged
    End Sub

    Private Sub OnStudioChanged(sender As Object, e As EventArgs)
        Try
            _sidebar.SetStudio(AppSettings.GetValue(AppSettings.BusinessName, "LENSYNC Photo Studio"))
        Catch ex As Exception
            AppLog.Write(ex, "Refreshing studio name")
        End Try
    End Sub

    Private Sub OnCurrentUserChanged(sender As Object, e As EventArgs)
        If Session.IsSignedIn Then _sidebar.SetUser(Session.CurrentUser.FullName, Session.CurrentUser.RoleName)
    End Sub

    ''' <summary>Asks the current view (e.g. Settings with unsaved edits) whether it may be left.</summary>
    Private Function CurrentViewCanLeave() As Boolean
        Dim guarded = TryCast(_current, IGuardedView)
        Return guarded Is Nothing OrElse guarded.CanLeave()
    End Function

    ' ---------------------------------------------------------------- layout
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If _sidebar Is Nothing Then Return
        Dim sideW = Theme.Dpi(248)
        Dim headH = Theme.Dpi(78)
        _sidebar.Bounds = New Rectangle(0, 0, sideW, ClientSize.Height)
        _header.Bounds = New Rectangle(sideW, 0, ClientSize.Width - sideW, headH)
        _content.Bounds = New Rectangle(sideW, headH, ClientSize.Width - sideW, ClientSize.Height - headH)
        If _current IsNot Nothing AndAlso Not _slideTween.IsRunning Then _current.Bounds = _content.ClientRectangle
    End Sub

    Private Sub LayoutSidebar()
        Dim w = _sidebar.Width
        Dim itemH = Theme.Dpi(46)
        Dim y = _sidebar.NavTop
        For Each b In _nav
            b.Bounds = New Rectangle(0, y, w, itemH)
            y += itemH + Theme.Dpi(2)
        Next
        Dim footH = Theme.Dpi(42)
        _btnSignOut.Bounds = New Rectangle(0, _sidebar.Height - Theme.Dpi(14) - footH, w, footH)
        _btnPassword.Bounds = New Rectangle(0, _btnSignOut.Top - footH, w, footH)
        _sidebar.FooterTop = _btnPassword.Top - Theme.Dpi(8)
    End Sub

    ' ---------------------------------------------------------------- startup
    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _clock.Start()
        If _nav.Count > 0 Then
            ShowModule(_nav(0).Key)
        Else
            ShowView(New PlaceholderView("No modules", "No modules assigned",
                                         "Your role does not have access to any module yet. Ask the Admin to grant access in Settings."),
                     "Welcome", "Logged in as " & Session.CurrentUser.FullName)
        End If
        Toast.Show($"Welcome, {Session.CurrentUser.FullName}.", ToastKind.Success)
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        ' Ctrl+1..9 opens the n-th module in the sidebar.
        If (keyData And Keys.Modifiers) = Keys.Control Then
            Dim k = keyData And Keys.KeyCode
            If k >= Keys.D1 AndAlso k <= Keys.D9 Then
                Dim index = CInt(k - Keys.D1)
                If index < _nav.Count Then
                    ShowModule(_nav(index).Key)
                    Return True
                End If
            End If
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    ' ---------------------------------------------------------------- modules
    ''' <summary>Used by views (e.g. a Dashboard tile) to open another module, optionally at a given tab.</summary>
    Public Sub OpenModule(key As String, Optional tab As Integer = 0) Implements IModuleHost.OpenModule
        ShowModule(key, tab)
    End Sub

    Private Sub ShowModule(key As String, Optional tab As Integer = 0)
        Dim info = AllModules.FirstOrDefault(Function(m) m.Key = key)
        If info Is Nothing Then Return
        ' Re-check here, not only when building the sidebar.
        If Not Session.HasPermission(key) Then
            AuditLog.Write(AuditActions.AccessDenied, "module", Nothing, key)
            LsDialog.Warning($"Your account doesn't have access to {info.Title}. Ask the Admin if you need it.", "No access")
            Return
        End If
        If key = _currentKey Then
            If tab > 0 Then TryCast(_current, TabbedView)?.SelectTab(tab)
            Return
        End If
        If Not CurrentViewCanLeave() Then Return
        _currentKey = key
        For Each b In _nav
            b.IsActive = b.Key = key
        Next
        Dim view = CreateView(info)
        Dim tabbed = TryCast(view, TabbedView)
        If tabbed IsNot Nothing Then tabbed.InitialTab = tab
        ShowView(view, info.Title, info.Subtitle)
    End Sub

    ''' <summary>Builds the view for a module. Later phases replace the placeholders here.</summary>
    Private Shared Function CreateView(info As ModuleInfo) As Control
        Select Case info.Key
            Case Permissions.Settings : Return New SettingsView()
            Case Permissions.AddService : Return New AddServiceView()
            Case Permissions.Dashboard : Return New DashboardView()
            Case Permissions.Reports : Return New ReportsView()
            Case Else : Return New PlaceholderView($"Arrives in Phase {info.Phase}", info.Title, info.Description)
        End Select
    End Function

    Private Sub ShowView(view As Control, title As String, subtitle As String)
        _header.SetTitle(title, subtitle)
        Dim old = _current
        _current = view
        Dim offset = Theme.Dpi(28)
        view.Bounds = New Rectangle(offset, 0, _content.ClientSize.Width, _content.ClientSize.Height)
        _content.Controls.Add(view)
        view.BringToFront()
        If old IsNot Nothing Then
            _content.Controls.Remove(old)
            old.Dispose()
        End If
        _slideTween.Start(Theme.OpenMs,
            Sub(t) view.Left = CInt(offset * (1 - Easing.OutCubic(t))),
            Sub() view.Bounds = _content.ClientRectangle)
    End Sub

    ' ---------------------------------------------------------------- account
    Private Sub OnChangePassword(sender As Object, e As EventArgs)
        Using dlg As New ChangePasswordDialog(forced:=False)
            dlg.ShowCard(Me)
        End Using
    End Sub

    Private Sub OnSignOut(sender As Object, e As EventArgs)
        If Not CurrentViewCanLeave() Then Return
        If Not LsDialog.Confirm("You'll return to the log-in screen.", "Log out of LENSYNC?", "Log out", "Stay logged in") Then Return
        ' Program ends the session after this window and its views are disposed.
        SignedOut = True
        Close()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' Closing the window with unsaved Settings edits asks first (Log out already asked).
        If e.CloseReason = CloseReason.UserClosing AndAlso Not SignedOut AndAlso Not CurrentViewCanLeave() Then
            e.Cancel = True
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        RemoveHandler AppEvents.StudioChanged, AddressOf OnStudioChanged
        RemoveHandler AppEvents.CurrentUserChanged, AddressOf OnCurrentUserChanged
        _clock.Stop()
        MyBase.OnFormClosed(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _slideTween.Dispose()
            _clock.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ' ==================================================================== sidebar
    ''' <summary>Ink sidebar background: brand block, MODULES caption, user block above the footer actions.</summary>
    Private NotInheritable Class SidebarPanel
        Inherits Panel

        Private _studio As String = "LENSYNC Photo Studio"
        Private _userName As String = ""
        Private _role As String = ""
        Private _footerTop As Integer

        Public Sub New()
            DoubleBuffered = True
            SetStyle(ControlStyles.ResizeRedraw, True)
            BackColor = Theme.Ink
        End Sub

        Public ReadOnly Property NavTop As Integer
            Get
                Return Theme.Dpi(132)
            End Get
        End Property

        Public Property FooterTop As Integer
            Get
                Return _footerTop
            End Get
            Set(value As Integer)
                If _footerTop <> value Then
                    _footerTop = value
                    Invalidate()
                End If
            End Set
        End Property

        Public Sub SetStudio(name As String)
            _studio = name
            Invalidate()
        End Sub

        Public Sub SetUser(name As String, role As String)
            _userName = name
            _role = role
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim pad = Theme.Dpi(24)

            ' Brand block.
            Dim markS = Theme.Dpi(30.0F)
            Gfx.DrawBrandMark(g, New RectangleF(pad, Theme.Dpi(30), markS, markS), Theme.White, Theme.Dpi(1.5F))
            Dim textX = pad + CInt(markS) + Theme.Dpi(14)
            Gfx.DrawTracked(g, "LENSYNC", Theme.BodyBold, Theme.White, textX, Theme.Dpi(28), Theme.Dpi(3.5F))
            TextRenderer.DrawText(g, _studio, Theme.Label, New Rectangle(textX, Theme.Dpi(28) + Theme.BodyBold.Height + Theme.Dpi(1),
                                  Width - textX - Theme.Dpi(12), Theme.Label.Height + 2), Theme.Silver,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
            Using p As New Pen(Theme.Charcoal)
                g.DrawLine(p, pad, Theme.Dpi(88), Width - pad, Theme.Dpi(88))
            End Using
            Gfx.DrawTracked(g, "MODULES", Theme.Caption, Theme.Slate, pad, Theme.Dpi(106), Theme.Dpi(2.2F))

            ' User block above the footer actions.
            If _footerTop <= 0 Then Return
            Dim blockH = Theme.Dpi(58)
            Dim top = _footerTop - blockH
            Using p As New Pen(Theme.Charcoal)
                g.DrawLine(p, pad, top - Theme.Dpi(8), Width - pad, top - Theme.Dpi(8))
            End Using
            Dim d = Theme.Dpi(36)
            Dim avatar As New Rectangle(pad, top + (blockH - d) \ 2, d, d)
            Using b As New SolidBrush(Theme.Charcoal)
                g.FillEllipse(b, avatar)
            End Using
            TextRenderer.DrawText(g, Initials(_userName), Theme.LabelBold, avatar, Theme.White,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
            Dim nameX = avatar.Right + Theme.Dpi(12)
            Dim nameW = Width - nameX - Theme.Dpi(12)
            Dim nameTop = top + (blockH - Theme.BodyBold.Height - Theme.Label.Height) \ 2
            TextRenderer.DrawText(g, _userName, Theme.BodyBold, New Rectangle(nameX, nameTop, nameW, Theme.BodyBold.Height), Theme.White,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
            TextRenderer.DrawText(g, _role, Theme.Label, New Rectangle(nameX, nameTop + Theme.BodyBold.Height, nameW, Theme.Label.Height), Theme.Silver,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
        End Sub

        Private Shared Function Initials(name As String) As String
            Dim parts = If(name, "").Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length = 0 Then Return "?"
            If parts.Length = 1 Then Return parts(0).Substring(0, 1).ToUpperInvariant()
            Return (parts(0).Substring(0, 1) & parts(parts.Length - 1).Substring(0, 1)).ToUpperInvariant()
        End Function

    End Class

    ' ==================================================================== header
    ''' <summary>White header: module title + subtitle on the left, live clock and date on the right.</summary>
    Private NotInheritable Class HeaderPanel
        Inherits Panel

        Private _title As String = ""
        Private _subtitle As String = ""
        Private _lastMinute As Integer = -1
        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")

        Public Sub New()
            DoubleBuffered = True
            SetStyle(ControlStyles.ResizeRedraw, True)
            BackColor = Theme.White
        End Sub

        Public Sub SetTitle(title As String, subtitle As String)
            _title = title
            _subtitle = subtitle
            Invalidate()
        End Sub

        ''' <summary>Called every second; repaints only when the minute changes.</summary>
        Public Sub Tick()
            Dim m = DateTime.Now.Minute
            If m <> _lastMinute Then
                _lastMinute = m
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim pad = Theme.Dpi(32)
            Dim now = DateTime.Now
            Dim time = now.ToString("h:mm tt", PH)
            Dim day = now.ToString("dddd, MMMM d, yyyy", PH)
            Dim rightW = Math.Max(TextRenderer.MeasureText(day, Theme.Label).Width, TextRenderer.MeasureText(time, Theme.BodyBold).Width) + Theme.Dpi(8)

            Dim blockH = Theme.Title.Height + Theme.Label.Height
            Dim top = (Height - blockH) \ 2
            Dim leftW = Width - pad * 2 - rightW - Theme.Dpi(16)
            TextRenderer.DrawText(g, _title, Theme.Title, New Rectangle(pad - Theme.Dpi(1), top, leftW, Theme.Title.Height), Theme.Ink,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
            TextRenderer.DrawText(g, _subtitle, Theme.Label, New Rectangle(pad, top + Theme.Title.Height, leftW, Theme.Label.Height), Theme.Slate,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)

            Dim rx = Width - pad - rightW
            Dim rTop = (Height - Theme.BodyBold.Height - Theme.Label.Height) \ 2
            TextRenderer.DrawText(g, time, Theme.BodyBold, New Rectangle(rx, rTop, rightW, Theme.BodyBold.Height), Theme.Ink,
                                  TextFormatFlags.Right Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
            TextRenderer.DrawText(g, day, Theme.Label, New Rectangle(rx, rTop + Theme.BodyBold.Height, rightW, Theme.Label.Height), Theme.Slate,
                                  TextFormatFlags.Right Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)

            Using p As New Pen(Theme.Mist)
                g.DrawLine(p, 0, Height - 1, Width, Height - 1)
            End Using
        End Sub

    End Class

End Class
