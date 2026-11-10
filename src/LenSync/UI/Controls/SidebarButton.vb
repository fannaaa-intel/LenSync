Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D

Namespace UI.Controls

    ''' <summary>
    ''' Navigation item on the ink sidebar. The active item is framed by white viewfinder brackets (it is "in focus"),
    ''' with white semibold text; others are silver and brighten on hover. Keyboard: Tab to it, Enter/Space to open.
    ''' </summary>
    Public Class SidebarButton
        Inherits Control

        Private _icon As LineIcon
        Private _active As Boolean
        Private _hoverT As Double
        Private _activeT As Double
        Private ReadOnly _hoverTween As New Tween()
        Private ReadOnly _activeTween As New Tween()

        Public Sub New(text As String, icon As LineIcon, Optional key As String = Nothing)
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable Or ControlStyles.StandardClick, True)
            Me.Text = text
            AccessibleName = text
            AccessibleRole = AccessibleRole.PushButton
            _icon = icon
            Me.Key = key
            TabStop = True
            Cursor = Cursors.Hand
            Font = Theme.Body
            BackColor = Theme.Ink
            Height = Theme.Dpi(46)
        End Sub

        ''' <summary>Permission key of the module this item opens (Nothing for actions like Sign out).</summary>
        Public Property Key As String

        Public Property IsActive As Boolean
            Get
                Return _active
            End Get
            Set(value As Boolean)
                If _active = value Then Return
                _active = value
                Dim from = _activeT
                Dim target = If(value, 1.0, 0.0)
                _activeTween.Start(Theme.OpenMs,
                    Sub(t)
                        _activeT = from + (target - from) * Easing.OutCubic(t)
                        Invalidate()
                    End Sub)
                Font = If(value, Theme.BodyBold, Theme.Body)
            End Set
        End Property

        Private Sub AnimateHover(target As Double)
            Dim from = _hoverT
            _hoverTween.Start(Theme.HoverMs,
                Sub(t)
                    _hoverT = from + (target - from) * Easing.OutCubic(t)
                    Invalidate()
                End Sub)
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            AnimateHover(1)
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            AnimateHover(0)
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Return keyData = Keys.Enter OrElse MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then
                e.Handled = True
                e.SuppressKeyPress = True
                OnClick(EventArgs.Empty)
            End If
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            Gfx.HighQuality(g)
            Dim inset = Theme.Dpi(10.0F)
            Dim frame As New RectangleF(inset, Theme.Dpi(4.0F), Width - inset * 2 - 1, Height - Theme.Dpi(8.0F) - 1)

            ' Hover wash (charcoal on ink).
            If _hoverT > 0.01 AndAlso _activeT < 0.99 Then
                Using path = Gfx.RoundedRect(frame, Theme.Dpi(CSng(Theme.RadiusSmall)))
                    Using b As New SolidBrush(Color.FromArgb(CInt(255 * _hoverT * (1 - _activeT)), Theme.Charcoal))
                        g.FillPath(b, path)
                    End Using
                End Using
            End If

            ' Active: viewfinder brackets close in around the item.
            If _activeT > 0.01 Then
                Dim grow = CSng(Theme.Dpi(6.0F) * (1 - _activeT))
                Dim r As New RectangleF(frame.X - grow, frame.Y - grow, frame.Width + grow * 2, frame.Height + grow * 2)
                Using pen As New Pen(Color.FromArgb(CInt(255 * _activeT), Theme.White), Theme.Dpi(1.75F))
                    pen.StartCap = LineCap.Square
                    pen.EndCap = LineCap.Square
                    Gfx.DrawViewfinder(g, r, Theme.Dpi(9.0F), pen)
                End Using
            End If

            ' Keyboard focus: a thin slate outline (only once the user navigates with the keyboard).
            If Focused AndAlso ShowFocusCues Then
                Using path = Gfx.RoundedRect(frame, Theme.Dpi(CSng(Theme.RadiusSmall)))
                    Using pen As New Pen(Theme.Slate, 1.0F) With {.DashStyle = DashStyle.Dot}
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End If

            Dim fore = Theme.Blend(Theme.Blend(Theme.Silver, Theme.White, _hoverT * 0.7), Theme.White, _activeT)
            Dim s = Theme.Dpi(18.0F)
            Dim iconX = inset + Theme.Dpi(14.0F)
            Gfx.DrawLineIcon(g, _icon, New RectangleF(iconX, (Height - s) / 2, s, s), fore, Theme.Dpi(If(_active, 1.75F, 1.5F)))
            Dim textX = CInt(iconX + s + Theme.Dpi(14))
            TextRenderer.DrawText(g, Text, Font, New Rectangle(textX, 0, Width - textX - CInt(inset), Height), fore,
                TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _hoverTween.Dispose()
                _activeTween.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
