Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Core

Namespace UI.Controls

    ''' <summary>
    ''' Live list of the password rules (Passwords.Rules). A met rule gets a filled check and ink text;
    ''' an unmet one an empty ring and slate text, so the state reads without color.
    ''' </summary>
    Public Class PasswordChecklist
        Inherits Control

        Private _password As String = ""

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            Font = Theme.Label
            Height = PreferredHeight
        End Sub

        Public ReadOnly Property RowHeight As Integer
            Get
                Return Math.Max(Theme.Dpi(20), Font.Height + Theme.Dpi(4))
            End Get
        End Property

        Public ReadOnly Property PreferredHeight As Integer
            Get
                Return RowHeight * Passwords.Rules.Count
            End Get
        End Property

        Public Sub Evaluate(password As String)
            _password = If(password, "")
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim rowH = RowHeight
            Dim d = Theme.Dpi(13.0F)
            For i = 0 To Passwords.Rules.Count - 1
                Dim rule = Passwords.Rules(i)
                Dim met = _password.Length > 0 AndAlso rule.Test(_password)
                Dim cy = i * rowH + rowH / 2.0F
                Dim c As New PointF(d / 2 + 1, cy)
                If met Then
                    Gfx.DrawLensIcon(g, c, d, IconKind.Check, 1, 1, Theme.White, Theme.Dpi(1.25F), True, Theme.Ink)
                Else
                    Using p As New Pen(Theme.Silver, Theme.Dpi(1.25F))
                        g.DrawEllipse(p, c.X - d / 2, c.Y - d / 2, d, d)
                    End Using
                End If
                Dim textRect As New Rectangle(CInt(d + Theme.Dpi(8)), i * rowH, Width - CInt(d) - Theme.Dpi(8), rowH)
                TextRenderer.DrawText(g, rule.Label, If(met, Theme.LabelBold, Theme.Label), textRect, If(met, Theme.Ink, Theme.Slate),
                    TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
            Next
        End Sub

    End Class

End Namespace
