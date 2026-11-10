Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports LenSync.UI

Namespace Modules

    ''' <summary>
    ''' Stand-in for a module that a later phase delivers (and for "no modules assigned").
    ''' Drawn, not built from controls, so it scales cleanly and never shows scrollbars.
    ''' </summary>
    Public Class PlaceholderView
        Inherits UserControl

        Private ReadOnly _caption As String
        Private ReadOnly _heading As String
        Private ReadOnly _message As String

        Public Sub New(caption As String, heading As String, message As String)
            _caption = caption
            _heading = heading
            _message = message
            AutoScaleMode = AutoScaleMode.None
            BackColor = Theme.Snow
            DoubleBuffered = True
            SetStyle(ControlStyles.ResizeRedraw, True)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim cardW = Math.Min(Theme.Dpi(560), Width - Theme.Dpi(48))
            Const wrap As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl
            Dim textW = cardW - Theme.Dpi(96)
            Dim msgH = TextRenderer.MeasureText(_message, Theme.Body, New Size(textW, Integer.MaxValue), wrap).Height
            Dim cardH = Theme.Dpi(200) + msgH
            Dim card As New Rectangle((Width - cardW) \ 2, Math.Max(Theme.Dpi(24), (Height - cardH) \ 2 - Theme.Dpi(20)), cardW, cardH)

            Using path = Gfx.RoundedRect(card, Theme.Dpi(CSng(Theme.Radius)))
                Using b As New SolidBrush(Theme.White)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(Theme.Mist)
                    g.DrawPath(p, path)
                End Using
            End Using
            Dim inset = Theme.Dpi(14.0F)
            Using pen As New Pen(Theme.Silver, Theme.Dpi(1.5F)) With {.StartCap = LineCap.Square, .EndCap = LineCap.Square}
                Gfx.DrawViewfinder(g, New RectangleF(card.X + inset, card.Y + inset, card.Width - inset * 2, card.Height - inset * 2), Theme.Dpi(18.0F), pen)
            End Using

            Dim cx = card.X + card.Width / 2.0F
            Dim y = card.Y + Theme.Dpi(40)
            Gfx.DrawBrandMark(g, New RectangleF(cx - Theme.Dpi(22), y, Theme.Dpi(44), Theme.Dpi(44)), Theme.Ink, Theme.Dpi(1.75F))
            y += Theme.Dpi(62)
            Gfx.DrawTracked(g, _caption.ToUpperInvariant(), Theme.Caption, Theme.Slate, cx, y, Theme.Dpi(2.2F), StringAlignment.Center)
            y += Theme.Caption.Height + Theme.Dpi(8)
            TextRenderer.DrawText(g, _heading, Theme.TitleSmall, New Rectangle(card.X, y, card.Width, Theme.TitleSmall.Height), Theme.Ink,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis)
            y += Theme.TitleSmall.Height + Theme.Dpi(6)
            TextRenderer.DrawText(g, _message, Theme.Body, New Rectangle(card.X + Theme.Dpi(48), y, textW, msgH), Theme.Slate, wrap)
        End Sub

    End Class

End Namespace
