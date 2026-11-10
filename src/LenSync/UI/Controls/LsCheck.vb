Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Controls

    ''' <summary>Themed checkbox: rounded square that fills with ink and draws a check; label to the right.</summary>
    <ComponentModel.DefaultEvent("CheckedChanged")>
    Public Class LsCheck
        Inherits Control

        Private _checked As Boolean
        Private _t As Double
        Private _hover As Boolean
        Private ReadOnly _tween As New Tween()

        Public Event CheckedChanged As EventHandler

        Public Sub New(Optional text As String = "")
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable Or ControlStyles.SupportsTransparentBackColor, True)
            Me.Text = text
            TabStop = True
            Cursor = Cursors.Hand
            Font = Theme.Body
            Height = Theme.Dpi(28)
            AccessibleRole = AccessibleRole.CheckButton
            AccessibleName = text
        End Sub

        Public Property Checked As Boolean
            Get
                Return _checked
            End Get
            Set(value As Boolean)
                If _checked = value Then Return
                _checked = value
                Dim from = _t
                Dim target = If(value, 1.0, 0.0)
                If IsHandleCreated Then
                    _tween.Start(Theme.HoverMs, Sub(t)
                                                    _t = from + (target - from) * t
                                                    Invalidate()
                                                End Sub)
                Else
                    _t = target
                End If
                RaiseEvent CheckedChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Public Function PreferredWidth() As Integer
            Return Theme.Dpi(30) + TextRenderer.MeasureText(Text, Font).Width
        End Function

        Protected Overrides Sub OnClick(e As EventArgs)
            MyBase.OnClick(e)
            If Enabled Then Checked = Not Checked
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
        End Sub

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Space Then Checked = Not Checked
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            _hover = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnTextChanged(e As EventArgs)
            MyBase.OnTextChanged(e)
            AccessibleName = Text
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim s = Theme.Dpi(18.0F)
            Dim box As New RectangleF(1, (Height - s) / 2, s, s)
            DrawBox(g, box, _t, Enabled, _hover OrElse Focused)
            TextRenderer.DrawText(g, Text, Font, New Rectangle(CInt(s) + Theme.Dpi(10), 0, Width - CInt(s) - Theme.Dpi(10), Height),
                                  If(Enabled, Theme.Charcoal, Theme.Silver),
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End Sub

        ''' <summary>Shared by LsCheck and the permission grid so every checkbox looks the same.</summary>
        Public Shared Sub DrawBox(g As Graphics, box As RectangleF, t As Double, enabled As Boolean, emphasize As Boolean)
            Using path = Gfx.RoundedRect(box, Theme.Dpi(4.0F))
                Dim fillColor = If(enabled, Theme.Ink, Theme.Silver)
                If t > 0.01 Then
                    Using b As New SolidBrush(Color.FromArgb(CInt(255 * Easing.Clamp(t)), fillColor))
                        g.FillPath(b, path)
                    End Using
                End If
                If t < 0.99 Then
                    Using p As New Pen(If(Not enabled, Theme.Mist, If(emphasize, Theme.Ink, Theme.Silver)), Theme.Dpi(1.5F))
                        g.DrawPath(p, path)
                    End Using
                End If
            End Using
            If t > 0.01 Then
                Using p As New Pen(Theme.White, Theme.Dpi(2.0F)) With {.StartCap = Drawing2D.LineCap.Round, .EndCap = Drawing2D.LineCap.Round, .LineJoin = Drawing2D.LineJoin.Round}
                    Dim x = box.X
                    Dim y = box.Y
                    Dim w = box.Width
                    Gfx.DrawPartialPolyline(g, p, t,
                        New PointF(x + w * 0.24F, y + w * 0.52F), New PointF(x + w * 0.43F, y + w * 0.7F), New PointF(x + w * 0.76F, y + w * 0.32F))
                End Using
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
