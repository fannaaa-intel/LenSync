Option Strict On
Option Explicit On
Option Infer On

Imports System.Diagnostics

Namespace UI.Dialogs

    Public Enum ToastKind
        Success
        [Error]
        Warning
        Info
    End Enum

    ''' <summary>
    ''' Non-blocking notification: an ink card that slides in at the bottom-right of the app window, stacks upward,
    ''' and fades out on its own. Never takes focus. Click to dismiss; hover to keep it open.
    ''' </summary>
    Public NotInheritable Class Toast

        Private Const MaxVisible As Integer = 4

        Private Shared ReadOnly _active As New List(Of ToastWindow)
        Private Shared _area As Rectangle

        Private Sub New()
        End Sub

        Public Shared Sub Show(message As String, Optional kind As ToastKind = ToastKind.Info)
            Dim anchor = LsDialog.ResolveOwner()
            If anchor IsNot Nothing Then
                Dim screenArea = Screen.FromControl(anchor).WorkingArea
                _area = Rectangle.Intersect(Native.VisibleBounds(anchor), screenArea)
                If _area.Width < Theme.Dpi(420) OrElse _area.Height < Theme.Dpi(200) Then _area = screenArea
            Else
                _area = Screen.PrimaryScreen.WorkingArea
            End If

            While _active.Count >= MaxVisible
                Dim oldest = _active(0)
                _active.RemoveAt(0)
                oldest.Dismiss()
            End While

            Dim toast As New ToastWindow(kind, If(message, ""))
            AddHandler toast.Dismissing, Sub(s, e)
                                             _active.Remove(toast)
                                             Restack()
                                         End Sub
            _active.Add(toast)
            Dim margin = Theme.Dpi(20)
            toast.ShowAt(_area.Right - margin - toast.Width, _area.Bottom - margin - toast.Height)
            Restack()
        End Sub

        ''' <summary>Newest toast sits at the bottom; older ones glide upward.</summary>
        Private Shared Sub Restack()
            Dim margin = Theme.Dpi(20)
            Dim gap = Theme.Dpi(10)
            Dim bottom = _area.Bottom - margin
            For i = _active.Count - 1 To 0 Step -1
                Dim t = _active(i)
                Dim top = bottom - t.Height
                t.GlideTo(top)
                bottom = top - gap
            Next
        End Sub

    End Class

    Friend NotInheritable Class ToastWindow
        Inherits Form

        Private Const BaseWidth As Integer = 360
        Private Const SlideDistance As Integer = 48

        Private ReadOnly _kind As ToastKind
        Private ReadOnly _message As String
        Private _iconCenter As PointF
        Private _iconDiameter As Single
        Private _captionPoint As Point
        Private _messageRect As Rectangle

        Private ReadOnly _openTween As New Tween()
        Private ReadOnly _glideTween As New Tween()
        Private ReadOnly _closeTween As New Tween()
        Private ReadOnly _lifeTimer As New Timer() With {.Interval = Theme.FrameMs}
        Private ReadOnly _lifeClock As New Stopwatch()
        Private _lifeElapsedMs As Double
        Private _openT As Double
        Private _restLeft As Integer
        Private _shown As Boolean
        Private _dismissed As Boolean

        Public Event Dismissing As EventHandler

        Public Sub New(kind As ToastKind, message As String)
            _kind = kind
            _message = message
            AutoScaleMode = AutoScaleMode.None
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            BackColor = Theme.Ink
            Font = Theme.Body
            Opacity = 0
            DoubleBuffered = True
            Cursor = Cursors.Hand
            SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.ResizeRedraw, True)
            AddHandler _lifeTimer.Tick, AddressOf OnLifeTick
            BuildLayout()
        End Sub

        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Dim cp = MyBase.CreateParams
                cp.ExStyle = cp.ExStyle Or Native.WS_EX_TOOLWINDOW Or Native.WS_EX_NOACTIVATE Or Native.WS_EX_TOPMOST
                cp.ClassStyle = cp.ClassStyle Or Native.CS_DROPSHADOW
                Return cp
            End Get
        End Property

        Private Sub BuildLayout()
            Dim w = Theme.Dpi(BaseWidth)
            Dim pad = Theme.Dpi(18)
            _iconDiameter = Theme.Dpi(28.0F)
            Dim textLeft = pad + CInt(_iconDiameter) + Theme.Dpi(14)
            Dim textW = w - textLeft - pad
            Dim padTop = Theme.Dpi(14)

            _captionPoint = New Point(textLeft, padTop)
            Dim msgTop = padTop + Theme.Caption.Height + Theme.Dpi(3)
            Const wrap As TextFormatFlags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl
            Dim msgH = TextRenderer.MeasureText(_message, Theme.Body, New Size(textW, Integer.MaxValue), wrap).Height
            msgH = Math.Min(msgH, Theme.Body.Height * 3)   ' at most three lines, then ellipsis
            _messageRect = New Rectangle(textLeft, msgTop, textW, msgH)

            Dim h = Math.Max(Theme.Dpi(68), msgTop + msgH + Theme.Dpi(18))
            ClientSize = New Size(w, h)
            _iconCenter = New PointF(pad + _iconDiameter / 2, h / 2.0F)
        End Sub

        ' ---------------------------------------------------------------- lifecycle
        Public Sub ShowAt(x As Integer, y As Integer)
            _restLeft = x
            Location = New Point(x + Theme.Dpi(SlideDistance), y)
            Show()
            Native.RoundCorners(Me, Theme.Dpi(Theme.Radius), small:=True)
            _shown = True
            _openTween.Start(Theme.OpenMs,
                Sub(t)
                    Dim k = Easing.OutCubic(t)
                    Opacity = k
                    Left = _restLeft + CInt(Theme.Dpi(SlideDistance) * (1 - k))
                    _openT = t
                    Invalidate()
                End Sub,
                Sub()
                    _lifeClock.Restart()
                    _lifeTimer.Start()
                End Sub)
        End Sub

        Public Sub GlideTo(targetTop As Integer)
            If _dismissed OrElse Top = targetTop Then Return
            Dim fromTop = Top
            _glideTween.Start(Theme.OpenMs, Sub(t) Top = fromTop + CInt((targetTop - fromTop) * Easing.OutCubic(t)))
        End Sub

        Private Sub OnLifeTick(sender As Object, e As EventArgs)
            Dim delta = _lifeClock.Elapsed.TotalMilliseconds
            _lifeClock.Restart()
            If Not Bounds.Contains(Cursor.Position) Then _lifeElapsedMs += delta
            Invalidate(New Rectangle(0, ClientSize.Height - Theme.Dpi(4), ClientSize.Width, Theme.Dpi(4)))
            If _lifeElapsedMs >= Theme.ToastVisibleMs Then Dismiss()
        End Sub

        Public Sub Dismiss()
            If _dismissed Then Return
            _dismissed = True
            _lifeTimer.Stop()
            _openTween.Stop()
            _glideTween.Stop()
            RaiseEvent Dismissing(Me, EventArgs.Empty)
            If Not _shown OrElse IsDisposed Then
                Dispose()
                Return
            End If
            Dim startOpacity = Opacity
            Dim startLeft = Left
            _closeTween.Start(Theme.CloseMs,
                Sub(t)
                    Dim k = Easing.OutCubic(t)
                    Opacity = startOpacity * (1 - k)
                    Left = startLeft + CInt(Theme.Dpi(24) * k)
                End Sub,
                Sub()
                    Close()
                    Dispose()
                End Sub)
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            Dismiss()
        End Sub

        ' ---------------------------------------------------------------- paint
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.Ink)
            Gfx.HighQuality(g)
            Dim open = Easing.OutCubic(_openT)
            Dim glyph = Easing.Clamp(_openT * 1.6 - 0.6)

            Gfx.DrawLensIcon(g, _iconCenter, _iconDiameter, IconFor(_kind), open, glyph, Theme.White, Theme.Dpi(1.75F))

            TextRenderer.DrawText(g, CaptionFor(_kind), Theme.Caption, _captionPoint, Theme.Silver,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding)
            TextRenderer.DrawText(g, _message, Theme.Body, _messageRect, Theme.White,
                                  TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl Or TextFormatFlags.EndEllipsis)

            ' Time-left hairline along the bottom edge.
            Dim remaining = 1 - Math.Min(1, _lifeElapsedMs / Theme.ToastVisibleMs)
            Dim thick = Math.Max(1, Theme.Dpi(2))
            Using b As New SolidBrush(Theme.Charcoal)
                g.FillRectangle(b, 0, ClientSize.Height - thick, ClientSize.Width, thick)
            End Using
            Using b As New SolidBrush(Theme.Slate)
                g.FillRectangle(b, 0, ClientSize.Height - thick, CInt(ClientSize.Width * remaining), thick)
            End Using
        End Sub

        Private Shared Function IconFor(kind As ToastKind) As IconKind
            Select Case kind
                Case ToastKind.Success : Return IconKind.Check
                Case ToastKind.Error : Return IconKind.Cross
                Case ToastKind.Warning : Return IconKind.Alert
                Case Else : Return IconKind.Info
            End Select
        End Function

        Private Shared Function CaptionFor(kind As ToastKind) As String
            Select Case kind
                Case ToastKind.Success : Return "SUCCESS"
                Case ToastKind.Error : Return "ERROR"
                Case ToastKind.Warning : Return "WARNING"
                Case Else : Return "NOTICE"
            End Select
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _openTween.Dispose()
                _glideTween.Dispose()
                _closeTween.Dispose()
                _lifeTimer.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
