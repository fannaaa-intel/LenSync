Option Strict On
Option Explicit On
Option Infer On

Imports System.Diagnostics
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
            Using dlg As New LsDialogWindow(kind, title, message, primaryText, secondaryText)
                dlg.ShowCard()
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
    ''' The message card: lens ring that sweeps closed around a drawn-in glyph, a faint focus ring that pulls in,
    ''' a spaced caption (SUCCESS / ERROR / ...), title, message and buttons. Enter = focused button, Esc = No/close.
    ''' </summary>
    Friend NotInheritable Class LsDialogWindow
        Inherits CardWindow

        Private Const BaseWidth As Integer = 400
        Private Const GlyphMs As Integer = 200

        Private ReadOnly _kind As DialogKind
        Private ReadOnly _title As String
        Private ReadOnly _message As String
        Private ReadOnly _primary As LsButton
        Private ReadOnly _secondary As LsButton

        Private _iconCenter As PointF
        Private _iconDiameter As Single
        Private _captionTop As Integer
        Private _titleRect As Rectangle
        Private _messageRect As Rectangle

        Private _glyphT As Double
        Private _countdown As Double = 1
        Private ReadOnly _glyphTween As New Tween()
        Private ReadOnly _countdownTimer As New Timer() With {.Interval = Theme.FrameMs}
        Private ReadOnly _countdownClock As New Stopwatch()
        Private _countdownElapsedMs As Double

        Public Property Choice As Boolean

        Public Sub New(kind As DialogKind, title As String, message As String, primaryText As String, secondaryText As String)
            _kind = kind
            _title = If(title, "")
            _message = If(message, "")
            Text = _title
            StrongFrame = kind = DialogKind.Error OrElse kind = DialogKind.Warning

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

        Protected Overrides Sub OnShown(e As EventArgs)
            If _secondary IsNot Nothing Then _secondary.Focus() Else _primary.Focus()
            MyBase.OnShown(e)
        End Sub

        Protected Overrides Sub OnOpened()
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

        Private Sub OnCountdownTick(sender As Object, e As EventArgs)
            Dim delta = _countdownClock.Elapsed.TotalMilliseconds
            _countdownClock.Restart()
            ' Hovering pauses the auto-close so the user can finish reading.
            If Not Bounds.Contains(Cursor.Position) Then _countdownElapsedMs += delta
            _countdown = Math.Max(0, 1 - _countdownElapsedMs / Theme.SuccessAutoCloseMs)
            Invalidate(New Rectangle(0, ClientSize.Height - Theme.Dpi(12), ClientSize.Width, Theme.Dpi(12)))
            If _countdown <= 0 Then Finish(True)
        End Sub

        Protected Overrides Sub OnEscapePressed()
            Finish(False)
        End Sub

        Private Sub Finish(choice As Boolean)
            If IsClosing Then Return
            Me.Choice = choice
            _countdownTimer.Stop()
            CloseCard(DialogResult.OK)
        End Sub

        Protected Overrides Sub PaintCard(g As Graphics)
            Dim w = ClientSize.Width
            Dim h = ClientSize.Height
            Dim open = Easing.OutCubic(OpenProgress)

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

            Gfx.DrawTracked(g, CaptionFor(_kind), Theme.Caption, Theme.Slate, w / 2.0F, _captionTop, Theme.Dpi(2.2F), StringAlignment.Center)

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
                _glyphTween.Dispose()
                _countdownTimer.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
