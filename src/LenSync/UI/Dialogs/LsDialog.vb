Option Strict On
Option Explicit On
Option Infer On

Imports System.Diagnostics
Imports System.Drawing.Drawing2D
Imports LenSync.UI.Controls

Namespace UI.Dialogs

    Public Enum DialogKind
        Success
        [Error]
        Warning
        Info
        Confirm
    End Enum

    ''' <summary>
    ''' LENSYNC's replacement for MessageBox. Every popup message in the app goes through here.
    ''' </summary>
    Public NotInheritable Class LsDialog

        Private Sub New()
        End Sub

        ''' <summary>Check icon; closes itself after ~2.5 s (hovering pauses the countdown).</summary>
        Public Shared Sub Success(message As String, Optional title As String = "All set")
            ShowCore(DialogKind.Success, title, message, "OK", Nothing)
        End Sub

        ''' <summary>X icon and a short horizontal shake.</summary>
        Public Shared Sub [Error](message As String, Optional title As String = "Something went wrong")
            ShowCore(DialogKind.Error, title, message, "OK", Nothing)
        End Sub

        Public Shared Sub Warning(message As String, Optional title As String = "Please check")
            ShowCore(DialogKind.Warning, title, message, "OK", Nothing)
        End Sub

        Public Shared Sub Info(message As String, Optional title As String = "Good to know")
            ShowCore(DialogKind.Info, title, message, "OK", Nothing)
        End Sub

        ''' <summary>Returns True only if the user picks <paramref name="yesText"/>. The safe option has focus.</summary>
        Public Shared Function Confirm(message As String, Optional title As String = "Are you sure?",
                                       Optional yesText As String = "Yes", Optional noText As String = "No") As Boolean
            Return ShowCore(DialogKind.Confirm, title, message, yesText, noText)
        End Function

        Private Shared Function ShowCore(kind As DialogKind, title As String, message As String,
                                         primaryText As String, secondaryText As String) As Boolean
            Dim owner = ResolveOwner()
            Dim overlay As DimOverlay = Nothing
            If owner IsNot Nothing Then
                overlay = New DimOverlay(owner)
                overlay.FadeIn()
            End If
            Using dlg As New LsDialogWindow(kind, title, message, primaryText, secondaryText, owner, overlay)
                If owner IsNot Nothing Then
                    dlg.ShowDialog(owner)
                Else
                    dlg.ShowDialog()
                End If
                Return dlg.Choice
            End Using
        End Function

        ''' <summary>The window to dim and center over: the active app window, else the newest visible one.</summary>
        Friend Shared Function ResolveOwner() As Form
            Dim active = Form.ActiveForm
            If IsOwnerCandidate(active) Then Return active
            For i = Application.OpenForms.Count - 1 To 0 Step -1
                Dim f = Application.OpenForms(i)
                If IsOwnerCandidate(f) Then Return f
            Next
            Return Nothing
        End Function

        Private Shared Function IsOwnerCandidate(f As Form) As Boolean
            Return f IsNot Nothing AndAlso Not f.IsDisposed AndAlso f.Visible AndAlso
                   f.WindowState <> FormWindowState.Minimized AndAlso
                   TypeOf f IsNot DimOverlay AndAlso TypeOf f IsNot ToastWindow
        End Function

    End Class

    ''' <summary>
    ''' The dialog card: white, viewfinder brackets that "focus" in, a lens ring that sweeps closed around the glyph,
    ''' a spaced caption (SUCCESS / ERROR / ...), title, message and buttons. Opens with fade + slide-up,
    ''' closes with fade + lift. Enter = focused button, Esc = No/close.
    ''' </summary>
    Friend NotInheritable Class LsDialogWindow
        Inherits Form

        Private Const BaseWidth As Integer = 400
        Private Const GlyphMs As Integer = 200
        Private Const SlideDistance As Integer = 16

        Private ReadOnly _kind As DialogKind
        Private ReadOnly _title As String
        Private ReadOnly _message As String
        Private ReadOnly _ownerForm As Form
        Private _overlay As DimOverlay
        Private ReadOnly _primary As LsButton
        Private ReadOnly _secondary As LsButton

        Private _iconCenter As PointF
        Private _iconDiameter As Single
        Private _captionTop As Integer
        Private _titleRect As Rectangle
        Private _messageRect As Rectangle

        Private _openT As Double
        Private _glyphT As Double
        Private _countdown As Double = 1
        Private ReadOnly _openTween As New Tween()
        Private ReadOnly _glyphTween As New Tween()
        Private ReadOnly _shakeTween As New Tween()
        Private ReadOnly _closeTween As New Tween()
        Private ReadOnly _countdownTimer As New Timer() With {.Interval = Theme.FrameMs}
        Private ReadOnly _countdownClock As New Stopwatch()
        Private _countdownElapsedMs As Double

        Private _rest As Point
        Private _closing As Boolean
        Private _closeAllowed As Boolean

        Public Property Choice As Boolean

        Public Sub New(kind As DialogKind, title As String, message As String, primaryText As String,
                       secondaryText As String, ownerForm As Form, overlay As DimOverlay)
            _kind = kind
            _title = If(title, "")
            _message = If(message, "")
            _ownerForm = ownerForm
            _overlay = overlay

            AutoScaleMode = AutoScaleMode.None
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            KeyPreview = True
            BackColor = Theme.White
            Font = Theme.Body
            Opacity = 0
            DoubleBuffered = True
            Text = _title
            SetStyle(ControlStyles.ResizeRedraw Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint, True)

            _primary = New LsButton(primaryText, ButtonVariant.Primary)
            AddHandler _primary.Click, Sub() Finish(True)
            If Not String.IsNullOrEmpty(secondaryText) Then
                _secondary = New LsButton(secondaryText, ButtonVariant.Secondary)
                AddHandler _secondary.Click, Sub() Finish(False)
                Controls.Add(_secondary)
                _secondary.TabIndex = 0
                _primary.TabIndex = 1
            End If
            Controls.Add(_primary)

            AddHandler _countdownTimer.Tick, AddressOf OnCountdownTick
            BuildLayout()
        End Sub

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Dim cp = MyBase.CreateParams
                cp.ClassStyle = cp.ClassStyle Or Native.CS_DROPSHADOW
                Return cp
            End Get
        End Property

        ' ---------------------------------------------------------------- layout
        Private Sub BuildLayout()
            Dim w = Theme.Dpi(BaseWidth)
            Dim pad = Theme.Dpi(36)
            Dim textW = w - pad * 2
            Dim y = Theme.Dpi(38)

            _iconDiameter = Theme.Dpi(54.0F)
            _iconCenter = New PointF(w / 2.0F, y + _iconDiameter / 2.0F)
            y += CInt(_iconDiameter) + Theme.Dpi(18)

            _captionTop = y
            y += Theme.Caption.Height + Theme.Dpi(6)

            Const wrap As TextFormatFlags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl
            Dim titleH = TextRenderer.MeasureText(_title, Theme.TitleSmall, New Size(textW, Integer.MaxValue), wrap).Height
            _titleRect = New Rectangle(pad, y, textW, titleH)
            y += titleH

            If _message.Length > 0 Then
                y += Theme.Dpi(8)
                ' Very long messages are capped (ellipsis) instead of growing a scrollbar.
                Dim msgH = TextRenderer.MeasureText(_message, Theme.Body, New Size(textW, Integer.MaxValue), wrap).Height
                msgH = Math.Min(msgH, Theme.Dpi(300))
                _messageRect = New Rectangle(pad, y, textW, msgH)
                y += msgH
            End If

            y += Theme.Dpi(28)
            If _secondary IsNot Nothing Then
                _primary.FitToText(128)
                _secondary.FitToText(128)
                Dim bw = Math.Max(_primary.Width, _secondary.Width)
                _primary.Width = bw
                _secondary.Width = bw
                Dim gap = Theme.Dpi(10)
                Dim x0 = (w - (bw * 2 + gap)) \ 2
                _secondary.Location = New Point(x0, y)
                _primary.Location = New Point(x0 + bw + gap, y)
            Else
                _primary.FitToText(150)
                _primary.Location = New Point((w - _primary.Width) \ 2, y)
            End If
            y += _primary.Height + Theme.Dpi(32)

            ClientSize = New Size(w, y)
        End Sub

        Private Function ComputeRestLocation() As Point
            Dim area As Rectangle
            If _ownerForm IsNot Nothing Then
                area = Native.VisibleBounds(_ownerForm)
            Else
                area = Screen.FromPoint(Cursor.Position).WorkingArea
            End If
            Dim x = area.Left + (area.Width - Width) \ 2
            Dim y = area.Top + (area.Height - Height) \ 2
            ' Keep the whole card on the screen even if the owner is partly off-screen.
            Dim wa = Screen.FromRectangle(area).WorkingArea
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width))
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height))
            Return New Point(x, y)
        End Function

        ' ---------------------------------------------------------------- open
        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            _rest = ComputeRestLocation()
            Location = New Point(_rest.X, _rest.Y + Theme.Dpi(SlideDistance))
            Native.RoundCorners(Me, Theme.Dpi(Theme.Radius))
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            If _secondary IsNot Nothing Then _secondary.Focus() Else _primary.Focus()
            _openTween.Start(Theme.OpenMs,
                Sub(t)
                    Dim k = Easing.OutCubic(t)
                    Opacity = k
                    Top = _rest.Y + CInt(Theme.Dpi(SlideDistance) * (1 - k))
                    _openT = t
                    Invalidate()
                End Sub,
                AddressOf OnOpened)
        End Sub

        Private Sub OnOpened()
            _glyphTween.Start(GlyphMs,
                Sub(t)
                    _glyphT = Easing.OutCubic(t)
                    Invalidate()
                End Sub)
            Select Case _kind
                Case DialogKind.Error
                    Shake()
                Case DialogKind.Success
                    _countdownClock.Restart()
                    _countdownTimer.Start()
            End Select
        End Sub

        Private Sub Shake()
            Dim amplitude = Theme.Dpi(10)
            _shakeTween.Start(Theme.ShakeMs,
                Sub(t) Left = _rest.X + CInt(amplitude * Easing.Shake(t)),
                Sub() Left = _rest.X)
        End Sub

        Private Sub OnCountdownTick(sender As Object, e As EventArgs)
            Dim delta = _countdownClock.Elapsed.TotalMilliseconds
            _countdownClock.Restart()
            ' Hovering pauses the auto-close so the user can finish reading.
            If Not Bounds.Contains(Cursor.Position) Then _countdownElapsedMs += delta
            _countdown = Math.Max(0, 1 - _countdownElapsedMs / Theme.SuccessAutoCloseMs)
            Invalidate(New Rectangle(0, ClientSize.Height - Theme.Dpi(12), ClientSize.Width, Theme.Dpi(12)))
            If _countdown <= 0 Then Finish(True)
        End Sub

        ' ---------------------------------------------------------------- close
        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If keyData = Keys.Escape Then
                Finish(False)
                Return True
            End If
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        Private Sub Finish(choice As Boolean)
            If _closing Then Return
            _closing = True
            Me.Choice = choice
            _countdownTimer.Stop()
            _shakeTween.Stop()
            _openTween.Stop()
            _overlay?.FadeOut()
            _overlay = Nothing

            Dim startOpacity = Opacity
            Dim startTop = Top
            Left = _rest.X
            _closeTween.Start(Theme.CloseMs,
                Sub(t)
                    Dim k = Easing.OutCubic(t)
                    Opacity = startOpacity * (1 - k)
                    Top = startTop - CInt(Theme.Dpi(8) * k)
                End Sub,
                Sub()
                    _closeAllowed = True
                    DialogResult = DialogResult.OK
                End Sub)
        End Sub

        Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
            ' Alt+F4 and similar go through the same animated path; system shutdown closes immediately.
            If Not _closeAllowed AndAlso e.CloseReason = CloseReason.UserClosing Then
                e.Cancel = True
                Finish(False)
                Return
            End If
            If _overlay IsNot Nothing Then
                _overlay.FadeOut()
                _overlay = Nothing
            End If
            MyBase.OnFormClosing(e)
        End Sub

        ' ---------------------------------------------------------------- paint
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.White)
            Gfx.HighQuality(g)
            Dim w = ClientSize.Width
            Dim h = ClientSize.Height
            Dim open = Easing.OutCubic(_openT)
            Dim strong = _kind = DialogKind.Error OrElse _kind = DialogKind.Warning

            ' Viewfinder brackets: heavier ink for errors/warnings (weight, not color, carries the urgency).
            Dim inset = Theme.Dpi(14.0F)
            Using pen As New Pen(If(strong, Theme.Ink, Theme.Silver), Theme.Dpi(If(strong, 2.0F, 1.5F)))
                pen.StartCap = LineCap.Square
                pen.EndCap = LineCap.Square
                Gfx.DrawViewfinder(g, New RectangleF(inset, inset, w - inset * 2 - 1, h - inset * 2 - 1), Theme.Dpi(18.0F), pen, open)
            End Using

            ' Focus ring: a faint outer ring that pulls in to the lens as the dialog opens.
            Dim focusD = _iconDiameter + Theme.Dpi(14.0F) + CSng(Theme.Dpi(26.0F) * (1 - open))
            Using pen As New Pen(Color.FromArgb(CInt(255 * open), Theme.Mist), Theme.Dpi(1.25F))
                g.DrawEllipse(pen, _iconCenter.X - focusD / 2, _iconCenter.Y - focusD / 2, focusD, focusD)
            End Using

            Dim filled = _kind = DialogKind.Success
            Gfx.DrawLensIcon(g, _iconCenter, _iconDiameter, IconFor(_kind), open, _glyphT,
                             If(filled, Theme.White, Theme.Ink), Theme.Dpi(2.25F), filled, Theme.Ink)
            If filled Then
                ' The white ring would vanish on a white card; outline the disk in ink.
                Using pen As New Pen(Theme.Ink, Theme.Dpi(2.25F))
                    Dim r = _iconDiameter / 2
                    Dim sweep = CSng(360 * open)
                    If sweep > 0.5F Then g.DrawArc(pen, _iconCenter.X - r, _iconCenter.Y - r, _iconDiameter, _iconDiameter, -90, sweep)
                End Using
            End If

            DrawTracked(g, CaptionFor(_kind), Theme.Caption, Theme.Slate, w / 2.0F, _captionTop, Theme.Dpi(2.2F))

            Const centered As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.WordBreak Or
                                                TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl
            TextRenderer.DrawText(g, _title, Theme.TitleSmall, _titleRect, Theme.Ink, centered)
            If _message.Length > 0 Then
                TextRenderer.DrawText(g, _message, Theme.Body, _messageRect, Theme.Charcoal, centered Or TextFormatFlags.EndEllipsis)
            End If

            If _kind = DialogKind.Success AndAlso _countdownTimer.Enabled Then
                ' Shutter-style countdown line: shrinks toward the center until the dialog closes itself.
                Dim trackW = w - Theme.Dpi(72)
                Dim y = h - Theme.Dpi(9)
                Dim thick = Theme.Dpi(2)
                Using b As New SolidBrush(Theme.Mist)
                    g.FillRectangle(b, (w - trackW) \ 2, y, trackW, thick)
                End Using
                Dim remainW = CInt(trackW * _countdown)
                Using b As New SolidBrush(Theme.Ink)
                    g.FillRectangle(b, (w - remainW) \ 2, y, remainW, thick)
                End Using
            End If
        End Sub

        ''' <summary>Draws letter-spaced text centered on <paramref name="centerX"/>.</summary>
        Private Shared Sub DrawTracked(g As Graphics, text As String, font As Font, color As Color,
                                       centerX As Single, top As Integer, tracking As Single)
            Const flags As TextFormatFlags = TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine
            Dim widths(text.Length - 1) As Integer
            Dim total As Single = 0
            For i = 0 To text.Length - 1
                widths(i) = TextRenderer.MeasureText(g, text(i).ToString(), font, New Size(Integer.MaxValue, Integer.MaxValue), flags).Width
                total += widths(i)
            Next
            total += tracking * (text.Length - 1)
            Dim x = centerX - total / 2
            For i = 0 To text.Length - 1
                TextRenderer.DrawText(g, text(i).ToString(), font, New Point(CInt(x), top), color, flags)
                x += widths(i) + tracking
            Next
        End Sub

        Private Shared Function IconFor(kind As DialogKind) As IconKind
            Select Case kind
                Case DialogKind.Success : Return IconKind.Check
                Case DialogKind.Error : Return IconKind.Cross
                Case DialogKind.Warning : Return IconKind.Alert
                Case DialogKind.Confirm : Return IconKind.Question
                Case Else : Return IconKind.Info
            End Select
        End Function

        Private Shared Function CaptionFor(kind As DialogKind) As String
            Select Case kind
                Case DialogKind.Success : Return "SUCCESS"
                Case DialogKind.Error : Return "ERROR"
                Case DialogKind.Warning : Return "WARNING"
                Case DialogKind.Confirm : Return "CONFIRM"
                Case Else : Return "INFORMATION"
            End Select
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _openTween.Dispose()
                _glyphTween.Dispose()
                _shakeTween.Dispose()
                _closeTween.Dispose()
                _countdownTimer.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
