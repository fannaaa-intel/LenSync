Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Sign-in screen (full window). Left: ink brand panel with lens rings and viewfinder frame.
''' Right: the sign-in form. Closes with DialogResult.OK once a user is signed in (and has replaced a temporary password).
''' </summary>
Public Class LoginForm
    Inherits Form

    Private ReadOnly _brand As New BrandPanel()
    Private ReadOnly _pane As New Panel()
    Private ReadOnly _form As New Panel()
    Private ReadOnly _lblUser As New Label()
    Private ReadOnly _lblPass As New Label()
    Private ReadOnly _capsHint As New Label()
    Private ReadOnly _txtUser As New LsTextBox()
    Private ReadOnly _txtPass As New LsTextBox()
    Private ReadOnly _notice As New InlineNotice()
    Private ReadOnly _btnLogIn As New LsButton("Log in", ButtonVariant.Primary)
    Private ReadOnly _btnForgot As New LsButton("Forgot password?", ButtonVariant.Ghost)
    Private ReadOnly _footer As New Label()
    Private ReadOnly _lockTimer As New Timer() With {.Interval = 500}
    Private ReadOnly _shakeTween As New Tween()
    Private ReadOnly _noticeTween As New Tween()
    Private _noticeSlot As Double

    Private _headingTop As Integer
    Private _subtitleTop As Integer
    Private _lockedUser As String
    Private _lockedUntil As DateTime
    Private _busy As Boolean

    Private Const FormWidth As Integer = 360
    Private Const Caption As String = "LOG IN"
    Private Const Heading As String = "Welcome back"
    Private Const Subtitle As String = "Log in with your LENSYNC account to continue."

    Public Sub New()
        AutoScaleMode = AutoScaleMode.None
        Text = "LENSYNC - Log in"
        BackColor = Theme.White
        Font = Theme.Body
        StartPosition = FormStartPosition.CenterScreen
        Dim wa = Screen.PrimaryScreen.WorkingArea
        Size = New Size(Math.Min(Theme.Dpi(1100), wa.Width), Math.Min(Theme.Dpi(700), wa.Height))
        MinimumSize = New Size(Math.Min(Theme.Dpi(900), wa.Width), Math.Min(Theme.Dpi(620), wa.Height))
        WindowState = FormWindowState.Maximized
        KeyPreview = True
        DoubleBuffered = True
        BuildLayout()
    End Sub

    Private Sub BuildLayout()
        _pane.BackColor = Theme.White
        _form.BackColor = Theme.White
        AddHandler _form.Paint, AddressOf PaintFormHeader
        Controls.Add(_pane)
        Controls.Add(_brand)
        _pane.Controls.Add(_form)

        StyleLabel(_lblUser, "Username")
        StyleLabel(_lblPass, "Password")
        _capsHint.Text = "Caps Lock is on"
        _capsHint.Font = Theme.LabelBold
        _capsHint.ForeColor = Theme.Ink
        _capsHint.TextAlign = ContentAlignment.MiddleRight
        _capsHint.Visible = False

        _txtUser.FieldName = "Username"
        _txtPass.FieldName = "Password"
        _txtUser.Icon = LineIcon.User
        _txtUser.Placeholder = "Your username"
        _txtUser.MaxLength = 50
        _txtPass.Icon = LineIcon.Lock
        _txtPass.Placeholder = "Your password"
        _txtPass.IsPassword = True
        _txtPass.MaxLength = Passwords.MaxLength

        _btnLogIn.Height = Theme.Dpi(44)
        _btnForgot.Height = Theme.Dpi(38)
        AcceptButton = _btnLogIn

        _footer.Text = "LENSYNC  ·  Billing and Payment System  ·  Cagayan State University - Aparri"
        _footer.Font = Theme.Hint
        _footer.ForeColor = Theme.Silver
        _footer.TextAlign = ContentAlignment.MiddleCenter
        _footer.AutoEllipsis = True
        _pane.Controls.Add(_footer)

        _form.Controls.AddRange({_lblUser, _capsHint, _txtUser, _lblPass, _txtPass, _notice, _btnLogIn, _btnForgot})
        _txtUser.TabIndex = 0
        _txtPass.TabIndex = 1
        _btnLogIn.TabIndex = 2
        _btnForgot.TabIndex = 3

        AddHandler _btnLogIn.Click, AddressOf OnSignInClick
        AddHandler _btnForgot.Click, AddressOf OnForgotClick
        AddHandler _txtPass.KeyUp, Sub() UpdateCapsHint()
        AddHandler _txtPass.InnerTextBox.Enter, Sub() UpdateCapsHint()
        AddHandler _txtPass.InnerTextBox.Leave, Sub() _capsHint.Visible = False
        AddHandler _txtUser.TextChanged, AddressOf OnCredentialsEdited
        AddHandler _txtPass.TextChanged, AddressOf OnCredentialsEdited
        AddHandler _lockTimer.Tick, Sub() RefreshLockNotice(False)
        AddHandler _notice.VisibleChanged, Sub() AnimateNoticeSlot()
    End Sub

    Private Shared Sub StyleLabel(lbl As Label, text As String)
        lbl.Text = text
        lbl.Font = Theme.LabelBold
        lbl.ForeColor = Theme.Charcoal
        lbl.AutoSize = False
        lbl.TextAlign = ContentAlignment.BottomLeft
    End Sub

    ' ---------------------------------------------------------------- layout
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If _brand Is Nothing Then Return
        Dim w = ClientSize.Width
        Dim h = ClientSize.Height
        Dim brandW = Math.Max(Theme.Dpi(340), CInt(w * 0.44))
        _brand.Bounds = New Rectangle(0, 0, brandW, h)
        _pane.Bounds = New Rectangle(brandW, 0, w - brandW, h)

        Dim fw = Theme.Dpi(FormWidth)
        Dim y = 0
        _headingTop = Theme.Caption.Height + Theme.Dpi(10)
        _subtitleTop = _headingTop + Theme.TitleLarge.Height + Theme.Dpi(2)
        y = _subtitleTop + Theme.Body.Height * 2 + Theme.Dpi(22)

        Dim labelH = Theme.LabelBold.Height + Theme.Dpi(4)
        Dim fieldH = Theme.Dpi(42)
        Dim errorRoom = Theme.Dpi(30)
        _lblUser.Bounds = New Rectangle(0, y, fw, labelH)
        _txtUser.Bounds = New Rectangle(0, y + labelH + Theme.Dpi(4), fw, fieldH)
        y = _txtUser.Bottom + errorRoom
        _lblPass.Bounds = New Rectangle(0, y, fw \ 2, labelH)
        _capsHint.Bounds = New Rectangle(fw \ 2, y, fw - fw \ 2, labelH)
        _txtPass.Bounds = New Rectangle(0, y + labelH + Theme.Dpi(4), fw, fieldH)
        ' errorRoom under the password leaves space for its validation line; the button follows right after.
        y = _txtPass.Bottom + errorRoom
        ' The notice opens a slot only while it is shown; the button glides down/up as the slot grows/shrinks.
        Dim noticeH = Theme.Dpi(50)
        Dim slot = CInt((noticeH + Theme.Dpi(14)) * Easing.OutCubic(_noticeSlot))
        _notice.Bounds = New Rectangle(0, y, fw, Math.Max(1, Math.Min(noticeH, slot)))
        y += slot
        _btnLogIn.Bounds = New Rectangle(0, y, fw, _btnLogIn.Height)
        y = _btnLogIn.Bottom + Theme.Dpi(8)
        _btnForgot.Bounds = New Rectangle(0, y, fw, _btnForgot.Height)
        y = _btnForgot.Bottom

        ' Center on the height WITHOUT the notice, so the fields never jump when a message appears.
        Dim restingH = y - slot
        Dim footerH = Theme.Hint.Height + Theme.Dpi(8)
        Dim usableH = _pane.Height - footerH - Theme.Dpi(16)
        Dim top = Math.Max(Theme.Dpi(16), (usableH - restingH) \ 2)
        Dim left = If(_shakeTween.IsRunning, _form.Left, (_pane.Width - fw) \ 2)
        _form.Bounds = New Rectangle(left, top, fw, y)
        _footer.Bounds = New Rectangle(Theme.Dpi(16), _pane.Height - footerH - Theme.Dpi(14), _pane.Width - Theme.Dpi(32), footerH)
    End Sub

    Private Sub AnimateNoticeSlot()
        Dim target = If(_notice.Visible, 1.0, 0.0)
        Dim from = _noticeSlot
        If from = target Then Return
        _noticeTween.Start(Theme.OpenMs,
            Sub(t)
                _noticeSlot = from + (target - from) * t
                PerformLayout()
            End Sub)
    End Sub

    Private Sub PaintFormHeader(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        Gfx.HighQuality(g)
        Gfx.DrawTracked(g, Caption, Theme.Caption, Theme.Slate, 0, 0, Theme.Dpi(2.6F))
        TextRenderer.DrawText(g, Heading, Theme.TitleLarge, New Point(0, _headingTop), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
        TextRenderer.DrawText(g, Subtitle, Theme.Body, New Rectangle(0, _subtitleTop, _form.Width, Theme.Body.Height * 2 + Theme.Dpi(4)),
                              Theme.Slate, TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
    End Sub

    ' ---------------------------------------------------------------- startup
    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _brand.Animate()
        _txtUser.Focus()
        ' Studio name for the brand panel, and an early warning if the database is down.
        Dim message As String = Nothing
        Dim studio As String = Nothing
        Dim address As String = Nothing
        Dim ok = Await Task.Run(
            Function()
                If Not Db.TestConnection(message) Then Return False
                Try
                    studio = AppSettings.GetValue(AppSettings.BusinessName)
                    address = AppSettings.GetValue(AppSettings.Address)
                Catch ex As Exception
                    AppLog.Write(ex, "Loading studio name")
                End Try
                Return True
            End Function)
        If IsDisposed Then Return
        If ok Then
            _brand.SetStudio(studio, address)
        Else
            _notice.ShowMessage(message, NoticeKind.Error)
        End If
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        ' Enter in the username box moves to the password (instead of trying to sign in with no password).
        If keyData = Keys.Enter AndAlso _txtUser.InnerTextBox.Focused AndAlso _txtPass.Text.Length = 0 AndAlso _txtUser.Text.Trim().Length > 0 Then
            _txtPass.Focus()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnActivated(e As EventArgs)
        MyBase.OnActivated(e)
        UpdateCapsHint()
    End Sub

    Private Sub UpdateCapsHint()
        _capsHint.Visible = _txtPass.InnerTextBox.Focused AndAlso IsKeyLocked(Keys.CapsLock)
    End Sub

    ''' <summary>Clears the password field without counting it as the user "correcting" (keeps the notice).</summary>
    Private Sub ClearPassword()
        _clearingByCode = True
        _txtPass.Text = ""
        _clearingByCode = False
    End Sub
    Private _clearingByCode As Boolean

    Private Sub OnCredentialsEdited(sender As Object, e As EventArgs)
        ' A stale "invalid" message goes away once the user starts correcting; lock/DB notices stay.
        If _clearingByCode Then Return
        If _notice.Visible AndAlso _notice.Kind = NoticeKind.Error AndAlso Not IsLockedFor(_txtUser.Text.Trim()) AndAlso Not _busy Then
            If _notice.Text.StartsWith(Auth.GenericInvalid, StringComparison.Ordinal) Then _notice.HideMessage()
        End If
    End Sub

    ' ---------------------------------------------------------------- sign in
    Private Async Sub OnSignInClick(sender As Object, e As EventArgs)
        If _busy Then Return
        Validator.ClearAll(_form)
        Dim username = _txtUser.Text.Trim()
        Dim password = _txtPass.Text
        If username.Length = 0 Then Validator.SetError(_txtUser, "Enter your username.")
        If password.Length = 0 Then Validator.SetError(_txtPass, "Enter your password.")
        If Validator.HasErrors(_form) Then
            Validator.FocusFirstError(_form)
            Return
        End If
        If IsLockedFor(username) Then
            RefreshLockNotice(True)
            ShakeForm()
            Return
        End If

        SetBusy(True)
        Dim result As SignInResult
        Try
            result = Await Task.Run(Function() Auth.SignIn(username, password))
        Catch ex As Exception
            AppLog.Write(ex, "Sign in")
            SetBusy(False)
            _notice.ShowMessage(Db.FriendlyMessage(ex), NoticeKind.Error)
            ShakeForm()
            Return
        End Try

        Select Case result.Status
            Case SignInStatus.Success
                ClearPassword()
                If result.User.MustChangePassword AndAlso Not ReplaceTemporaryPassword() Then
                    SetBusy(False)
                    _notice.ShowMessage("You need to set your own password before using LENSYNC. Log in again when ready.", NoticeKind.Info)
                    _txtPass.Focus()
                    Return
                End If
                DialogResult = DialogResult.OK
                Close()
                Return
            Case SignInStatus.InvalidCredentials
                Dim msg = Auth.GenericInvalid
                If result.AttemptsLeft <= 2 Then
                    msg &= $" {result.AttemptsLeft} {If(result.AttemptsLeft = 1, "try", "tries")} left before a {Passwords.LockMinutes}-minute lock."
                End If
                _notice.ShowMessage(msg, NoticeKind.Error)
            Case SignInStatus.Locked
                _lockedUser = username
                _lockedUntil = DateTime.Now.AddSeconds(result.SecondsLocked)
                _lockTimer.Start()
                RefreshLockNotice(True)
            Case SignInStatus.Inactive
                _notice.ShowMessage("This account is deactivated. Please contact the administrator.", NoticeKind.Error)
        End Select
        SetBusy(False)
        ClearPassword()
        _txtPass.Focus()
        ShakeForm()
    End Sub

    ''' <summary>First sign-in with the temporary password: must set a new one before entering.</summary>
    Private Function ReplaceTemporaryPassword() As Boolean
        Using dlg As New ChangePasswordDialog(forced:=True)
            If dlg.ShowCard(Me) = DialogResult.OK Then Return True
        End Using
        Auth.SignOut("Left the first log-in password change")
        Return False
    End Function

    Private Function IsLockedFor(username As String) As Boolean
        Return _lockedUser IsNot Nothing AndAlso String.Equals(_lockedUser, username, StringComparison.OrdinalIgnoreCase) AndAlso
               DateTime.Now < _lockedUntil
    End Function

    Private Sub RefreshLockNotice(animate As Boolean)
        If _lockedUser Is Nothing Then
            _lockTimer.Stop()
            Return
        End If
        Dim left = _lockedUntil - DateTime.Now
        If left.TotalSeconds <= 0 Then
            _lockTimer.Stop()
            _lockedUser = Nothing
            _notice.ShowMessage("You can try logging in again.", NoticeKind.Info)
            Return
        End If
        Dim clock = $"{CInt(Math.Floor(left.TotalMinutes))}:{left.Seconds:00}"
        _notice.ShowMessage($"Too many failed attempts. For your security, try again in {clock}.", NoticeKind.Error, animate)
    End Sub

    Private Sub SetBusy(busy As Boolean)
        _busy = busy
        _btnLogIn.Enabled = Not busy
        _btnForgot.Enabled = Not busy
        _txtUser.Enabled = Not busy
        _txtPass.Enabled = Not busy
        _btnLogIn.Text = If(busy, "Logging in...", "Log in")
        UseWaitCursor = busy
    End Sub

    Private Sub ShakeForm()
        Dim restX = (_pane.Width - _form.Width) \ 2
        Dim amplitude = Theme.Dpi(8)
        _shakeTween.Start(Theme.ShakeMs,
            Sub(t) _form.Left = restX + CInt(amplitude * Easing.Shake(t)),
            Sub() _form.Left = restX)
    End Sub

    ' ---------------------------------------------------------------- forgot password
    Private Sub OnForgotClick(sender As Object, e As EventArgs)
        Using dlg As New ForgotPasswordDialog(_txtUser.Text.Trim())
            If dlg.ShowCard(Me) = DialogResult.OK Then
                _lockedUser = Nothing
                _lockTimer.Stop()
                _notice.ShowMessage("Password changed. Log in with your new password.", NoticeKind.Success)
                ClearPassword()
                _txtPass.Focus()
            End If
        End Using
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _lockTimer.Dispose()
            _shakeTween.Dispose()
            _noticeTween.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ' ==================================================================== brand panel
    ''' <summary>Ink panel: concentric lens rings, a viewfinder frame that focuses in, wordmark and studio details.</summary>
    Private NotInheritable Class BrandPanel
        Inherits Control

        Private ReadOnly _tween As New Tween()
        Private _t As Double
        Private _studio As String = "LENSYNC Photo Studio"
        Private _address As String = ""

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw, True)
            SetStyle(ControlStyles.Selectable, False)
            BackColor = Theme.Ink
        End Sub

        Public Sub Animate()
            _tween.Start(Theme.OpenMs + 120, Sub(t)
                                                 _t = t
                                                 Invalidate()
                                             End Sub)
        End Sub

        Public Sub SetStudio(name As String, address As String)
            If Not String.IsNullOrWhiteSpace(name) Then _studio = name
            _address = If(address, "")
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.Ink)
            Gfx.HighQuality(g)
            Dim w = Width
            Dim h = Height
            Dim k = Easing.OutCubic(_t)
            ' One center for the lens rings and the brand mark, so the mark sits dead-center in the lens.
            Dim c As New PointF(w / 2.0F, h * 0.4F)

            ' Lens rings: they settle inward as the screen opens (a focus pull).
            Dim baseR = Math.Min(w, h) * 0.36F
            For i = 0 To 4
                Dim r = baseR * (1 - i * 0.16F) + CSng(Theme.Dpi(30.0F) * (1 - k))
                Using p As New Pen(Color.FromArgb(CInt(255 * k), Theme.Charcoal), If(i = 0, Theme.Dpi(1.5F), 1.0F))
                    g.DrawEllipse(p, c.X - r, c.Y - r, r * 2, r * 2)
                End Using
            Next

            ' Viewfinder frame around the panel.
            Dim inset = Theme.Dpi(32.0F)
            Using pen As New Pen(Theme.Slate, Theme.Dpi(1.5F)) With {.StartCap = LineCap.Square, .EndCap = LineCap.Square}
                Gfx.DrawViewfinder(g, New RectangleF(inset, inset, w - inset * 2, h - inset * 2), Theme.Dpi(30.0F), pen, k)
            End Using

            ' Top corners: camera-display style captions.
            Dim capTop = CInt(inset + Theme.Dpi(18))
            Dim tracking = Theme.Dpi(2.0F)
            Const motto As String = "BILLING · PAYMENTS · REPORTS"
            Dim today = DateTime.Now.ToString("ddd · dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            Dim room = w - inset * 2 - Theme.Dpi(36)
            ' On a narrow panel the motto gives way so the two captions never overlap.
            If Gfx.MeasureTracked(g, motto, Theme.Caption, tracking) + Gfx.MeasureTracked(g, today, Theme.Caption, tracking) + Theme.Dpi(24) <= room Then
                Gfx.DrawTracked(g, motto, Theme.Caption, Theme.Silver, inset + Theme.Dpi(18), capTop, tracking)
            End If
            Gfx.DrawTracked(g, today, Theme.Caption, Theme.Silver, w - inset - Theme.Dpi(18), capTop, tracking, StringAlignment.Far)

            ' Mark centered on the rings; wordmark and tagline below it.
            Dim markS = Theme.Dpi(58.0F)
            Gfx.DrawBrandMark(g, New RectangleF(c.X - markS / 2, c.Y - markS / 2, markS, markS), Theme.White, Theme.Dpi(2.0F))
            Dim wordTop = CInt(c.Y + markS / 2 + Theme.Dpi(30))
            Gfx.DrawTracked(g, "LENSYNC", Theme.TitleLarge, Theme.White, c.X, wordTop, Theme.Dpi(9.0F), StringAlignment.Center)
            Dim tagRect As New Rectangle(CInt(inset * 2), wordTop + Theme.TitleLarge.Height + Theme.Dpi(10), CInt(w - inset * 4), Theme.Body.Height * 2 + 4)
            TextRenderer.DrawText(g, "Billing and payment system for photography studios", Theme.Body, tagRect, Theme.Silver,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix)

            ' Studio details, bottom-left inside the frame.
            Dim left = CInt(inset + Theme.Dpi(18))
            Dim bottom = CInt(h - inset - Theme.Dpi(18))
            Dim addrH = If(_address.Length > 0, Theme.Label.Height, 0)
            Dim nameTop = bottom - addrH - Theme.BodyBold.Height
            Dim textW = CInt(w - inset * 2 - Theme.Dpi(36))
            TextRenderer.DrawText(g, _studio, Theme.BodyBold, New Rectangle(left, nameTop, textW, Theme.BodyBold.Height), Theme.White,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
            If addrH > 0 Then
                TextRenderer.DrawText(g, _address, Theme.Label, New Rectangle(left, nameTop + Theme.BodyBold.Height, textW, addrH), Theme.Silver,
                                      TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Class
