Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Core
Imports LenSync.Models

Namespace UI.Controls

    ''' <summary>
    ''' The running totals of a bill on a snow card framed by viewfinder brackets: subtotal, discount, total, less
    ''' downpayment, and the balance due in large type.
    ''' </summary>
    Public Class BillSummary
        Inherits Control

        Private _totals As New BillTotals()
        Private _note As String = ""

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw, True)
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            BackColor = Theme.White
            AccessibleRole = AccessibleRole.StaticText
        End Sub

        ''' <summary><paramref name="discountNote"/>: e.g. "10% · Loyal customer" (shown under the discount).</summary>
        Public Sub SetTotals(totals As BillTotals, discountNote As String)
            _totals = totals
            _note = If(discountNote, "")
            AccessibleName = $"Balance due {Money.Format(totals.BalanceDue)}"
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.White)
            Gfx.HighQuality(g)
            Using path = Gfx.RoundedRect(New RectangleF(0, 0, Width - 1, Height - 1), Theme.Dpi(CSng(Theme.Radius)))
                Using b As New SolidBrush(Theme.Snow)
                    g.FillPath(b, path)
                End Using
            End Using
            Dim inset = Theme.Dpi(6.0F)
            Using p As New Pen(Theme.Silver, Theme.Dpi(1.25F))
                Gfx.DrawViewfinder(g, New RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1), Theme.Dpi(12.0F), p)
            End Using

            Dim pad = Theme.Dpi(20)
            Dim w = Width - pad * 2
            Dim y = Theme.Dpi(16)
            y = Row(g, "Subtotal", Money.Format(_totals.Subtotal), pad, w, y, False)
            y = Row(g, "Discount", If(_totals.DiscountAmount > 0D, "−" & Money.Format(_totals.DiscountAmount), "None"), pad, w, y, False)
            If _note.Length > 0 Then
                TextRenderer.DrawText(g, _note, Theme.Hint, New Rectangle(pad, y - Theme.Dpi(4), w, Theme.Hint.Height + 2), Theme.Slate,
                                      TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding)
                y += Theme.Hint.Height
            End If
            y = Row(g, "Total", Money.Format(_totals.Total), pad, w, y, True)
            y = Row(g, "Less downpayment", If(_totals.Downpayment > 0D, "−" & Money.Format(_totals.Downpayment), "None"), pad, w, y, False)
            y += Theme.Dpi(4)
            Using p As New Pen(Theme.Mist)
                g.DrawLine(p, pad, y, pad + w, y)
            End Using
            y += Theme.Dpi(8)
            Dim bigH = Theme.TitleSmall.Height
            Gfx.DrawTracked(g, "BALANCE DUE", Theme.Caption, Theme.Slate, pad, y + (bigH - Theme.Caption.Height) \ 2, Theme.Dpi(2.0F))
            TextRenderer.DrawText(g, Money.Format(_totals.BalanceDue), Theme.TitleSmall, New Rectangle(pad, y, w, bigH), Theme.Ink,
                                  TextFormatFlags.Right Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding)
        End Sub

        Private Shared Function Row(g As Graphics, label As String, value As String, x As Integer, w As Integer, y As Integer, strong As Boolean) As Integer
            Dim h = Theme.Body.Height + Theme.Dpi(7)
            Const flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding Or TextFormatFlags.VerticalCenter
            TextRenderer.DrawText(g, label, If(strong, Theme.BodyBold, Theme.Body), New Rectangle(x, y, w, h), If(strong, Theme.Ink, Theme.Charcoal), flags)
            TextRenderer.DrawText(g, value, If(strong, Theme.BodyBold, Theme.Body), New Rectangle(x, y, w, h), Theme.Ink, flags Or TextFormatFlags.Right)
            Return y + h
        End Function

    End Class

End Namespace
