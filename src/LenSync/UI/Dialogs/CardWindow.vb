Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D

Namespace UI.Dialogs

    ''' <summary>
    ''' Base for every LENSYNC popup: borderless white card with viewfinder brackets that "focus" in, a dimmed overlay
    ''' over the owner window, fade + slide-up on open, fade + lift on close, and an error shake.
    ''' Show it with ShowCard (not ShowDialog) so the overlay is managed.
    ''' </summary>
    Public MustInherit Class CardWindow
        Inherits Form

        Private Const SlideDistance As Integer = 16

        Private _overlay As DimOverlay
        Private _ownerForm As Form
        Private ReadOnly _openTween As New Tween()
        Private ReadOnly _shakeTween As New Tween()
        Private ReadOnly _closeTween As New Tween()
        Private _openT As Double
        Private _rest As Point
        Private _closing As Boolean
        Private _closeAllowed As Boolean
        Private _loaded As Boolean

        Protected Sub New()
            AutoScaleMode = AutoScaleMode.None
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            KeyPreview = True
            BackColor = Theme.White
            Font = Theme.Body
            Opacity = 0
            DoubleBuffered = True
            SetStyle(ControlStyles.ResizeRedraw Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint, True)
        End Sub

        ''' <summary>Heavier ink brackets (errors, warnings, security prompts).</summary>
        Protected Property StrongFrame As Boolean

        ''' <summary>Open animation progress 0..1 (for subclasses that animate their own content).</summary>
        Protected ReadOnly Property OpenProgress As Double
            Get
                Return _openT
            End Get
        End Property

        Protected ReadOnly Property IsClosing As Boolean
            Get
                Return _closing
            End Get
        End Property

        ''' <summary>Shows the card modally over <paramref name="owner"/> (default: the active app window).</summary>
        Public Function ShowCard(Optional owner As Form = Nothing) As DialogResult
            _ownerForm = If(owner, LsDialog.ResolveOwner())
            If _ownerForm IsNot Nothing Then
                _overlay = New DimOverlay(_ownerForm)
                _overlay.FadeIn()
                Return ShowDialog(_ownerForm)
            End If
            Return ShowDialog()
        End Function

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Dim cp = MyBase.CreateParams
                cp.ClassStyle = cp.ClassStyle Or Native.CS_DROPSHADOW
                Return cp
            End Get
        End Property

        ' ---------------------------------------------------------------- open
        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            _rest = ComputeRestLocation()
            Location = New Point(_rest.X, _rest.Y + Theme.Dpi(SlideDistance))
            Native.RoundCorners(Me, Theme.Dpi(Theme.Radius))
            _loaded = True
        End Sub

        Private Function ComputeRestLocation() As Point
            Dim area = If(_ownerForm IsNot Nothing, Native.VisibleBounds(_ownerForm), Screen.FromPoint(Cursor.Position).WorkingArea)
            Dim wa = Screen.FromRectangle(area).WorkingArea
            Dim x = area.Left + (area.Width - Width) \ 2
            Dim y = area.Top + (area.Height - Height) \ 2
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width))
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height))
            Return New Point(x, y)
        End Function

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
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

        ''' <summary>Runs once the open animation has finished.</summary>
        Protected Overridable Sub OnOpened()
        End Sub

        ''' <summary>Call after changing the card height so it stays centered where it was.</summary>
        Protected Sub KeepCentered(oldHeight As Integer)
            If Not _loaded Then Return
            _rest.Y += (oldHeight - Height) \ 2
            If Not _openTween.IsRunning Then Top = _rest.Y
        End Sub

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            ' Windows 10 fallback uses a Region; it must follow the size. (Windows 11 rounds natively: Region is Nothing.)
            If _loaded AndAlso Region IsNot Nothing Then Native.RoundCorners(Me, Theme.Dpi(Theme.Radius))
        End Sub

        ' ---------------------------------------------------------------- shake / close
        Protected Sub Shake()
            If _closing OrElse Not _loaded Then Return
            Dim amplitude = Theme.Dpi(10)
            _shakeTween.Start(Theme.ShakeMs,
                Sub(t) Left = _rest.X + CInt(amplitude * Easing.Shake(t)),
                Sub() Left = _rest.X)
        End Sub

        ''' <summary>Animates out, then closes with <paramref name="result"/>.</summary>
        Protected Sub CloseCard(result As DialogResult)
            If _closing Then Return
            _closing = True
            _openTween.Stop()
            _shakeTween.Stop()
            _overlay?.FadeOut()
            _overlay = Nothing
            Left = _rest.X
            Dim startOpacity = Opacity
            Dim startTop = Top
            _closeTween.Start(Theme.CloseMs,
                Sub(t)
                    Dim k = Easing.OutCubic(t)
                    Opacity = startOpacity * (1 - k)
                    Top = startTop - CInt(Theme.Dpi(8) * k)
                End Sub,
                Sub()
                    _closeAllowed = True
                    DialogResult = If(result = DialogResult.None, DialogResult.Cancel, result)
                End Sub)
        End Sub

        ''' <summary>Esc (and Alt+F4). Default: close as Cancel.</summary>
        Protected Overridable Sub OnEscapePressed()
            CloseCard(DialogResult.Cancel)
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            If keyData = Keys.Escape Then
                OnEscapePressed()
                Return True
            End If
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
            If Not _closeAllowed AndAlso e.CloseReason = CloseReason.UserClosing Then
                e.Cancel = True
                OnEscapePressed()
                Return
            End If
            If _overlay IsNot Nothing Then
                _overlay.FadeOut()
                _overlay = Nothing
            End If
            MyBase.OnFormClosing(e)
        End Sub

        ' ---------------------------------------------------------------- paint
        Protected NotOverridable Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.White)
            Gfx.HighQuality(g)
            Dim inset = Theme.Dpi(14.0F)
            Using pen As New Pen(If(StrongFrame, Theme.Ink, Theme.Silver), Theme.Dpi(If(StrongFrame, 2.0F, 1.5F)))
                pen.StartCap = LineCap.Square
                pen.EndCap = LineCap.Square
                Gfx.DrawViewfinder(g, New RectangleF(inset, inset, ClientSize.Width - inset * 2 - 1, ClientSize.Height - inset * 2 - 1),
                                   Theme.Dpi(18.0F), pen, Easing.OutCubic(_openT))
            End Using
            PaintCard(g)
        End Sub

        ''' <summary>Subclass content painting (brackets are already drawn).</summary>
        Protected Overridable Sub PaintCard(g As Graphics)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _openTween.Dispose()
                _shakeTween.Dispose()
                _closeTween.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
