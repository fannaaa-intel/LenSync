Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Controls

    Public Enum NoticeKind
        [Error]
        Info
        Success
    End Enum

    ''' <summary>
    ''' A message strip inside a form (e.g. "Invalid username or password."): snow background, ink edge bar and a lens
    ''' icon that draws itself in. Errors carry heavier weight (thicker bar, semibold text); never color.
    ''' Reserve its height in the layout so showing it doesn't push the form around.
    ''' </summary>
    Public Class InlineNotice
        Inherits Control

        Private _kind As NoticeKind = NoticeKind.Error
        Private _t As Double = 1
        Private ReadOnly _tween As New Tween()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            Font = Theme.Label
            Visible = False
        End Sub

        Public ReadOnly Property Kind As NoticeKind
            Get
                Return _kind
            End Get
        End Property

        ''' <summary>Shows (or updates) the message. Pass animate:=False for countdown refreshes.</summary>
        Public Sub ShowMessage(message As String, kind As NoticeKind, Optional animate As Boolean = True)
            _kind = kind
            Text = message
            AccessibleName = message
            Visible = True
            If animate Then
                _tween.Start(260, Sub(t)
                                      _t = t
                                      Invalidate()
                                  End Sub)
            Else
                Invalidate()
            End If
        End Sub

        Public Sub HideMessage()
            _tween.Stop()
            Visible = False
        End Sub

        Protected Overrides Sub OnTextChanged(e As EventArgs)
            MyBase.OnTextChanged(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim r As New RectangleF(0, 0, Width - 1, Height - 1)
            Using path = Gfx.RoundedRect(r, Theme.Dpi(CSng(Theme.RadiusSmall)))
                Using b As New SolidBrush(Theme.Snow)
                    g.FillPath(b, path)
                End Using
            End Using
            Dim strong = _kind = NoticeKind.Error
            Dim barW = Theme.Dpi(If(strong, 4, 2))
            ' The edge bar grows from the middle as the notice appears.
            Dim barH = CInt((Height - Theme.Dpi(12)) * Easing.OutCubic(_t))
            Using b As New SolidBrush(Theme.Ink)
                g.FillRectangle(b, 0, (Height - barH) \ 2, barW, barH)
            End Using

            Dim iconD = Theme.Dpi(18.0F)
            Dim iconX = Theme.Dpi(16.0F)
            Dim kindIcon = If(_kind = NoticeKind.Success, IconKind.Check, If(_kind = NoticeKind.Info, IconKind.Info, IconKind.Alert))
            Gfx.DrawLensIcon(g, New PointF(iconX + iconD / 2, Height / 2.0F), iconD, kindIcon,
                             Easing.OutCubic(_t), Easing.Clamp(_t * 1.6 - 0.6), Theme.Ink, Theme.Dpi(1.5F))

            Dim textLeft = CInt(iconX + iconD + Theme.Dpi(10))
            Dim textRect As New Rectangle(textLeft, Theme.Dpi(4), Width - textLeft - Theme.Dpi(10), Height - Theme.Dpi(8))
            TextRenderer.DrawText(g, Text, If(strong, Theme.LabelBold, Theme.Label), textRect, Theme.Ink,
                TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or
                TextFormatFlags.EndEllipsis Or TextFormatFlags.TextBoxControl)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
