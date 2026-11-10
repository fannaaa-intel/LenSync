Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Drawing.Text

Namespace UI

    ''' <summary>Glyphs drawn inside the lens ring of dialogs and toasts.</summary>
    Public Enum IconKind
        Check
        Cross
        Alert
        Info
        Question
    End Enum

    ''' <summary>GDI+ drawing helpers shared by the custom-drawn controls.</summary>
    Public NotInheritable Class Gfx

        Private Sub New()
        End Sub

        Public Shared Sub HighQuality(g As Graphics)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit
        End Sub

        Public Shared Function RoundedRect(r As RectangleF, radius As Single) As GraphicsPath
            Dim path As New GraphicsPath()
            Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
            If d <= 0.5F Then
                path.AddRectangle(r)
                Return path
            End If
            path.AddArc(r.X, r.Y, d, d, 180, 90)
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90)
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
            path.CloseFigure()
            Return path
        End Function

        ''' <summary>
        ''' Camera viewfinder corner brackets: LENSYNC's signature decoration. <paramref name="t"/> (0..1) grows the
        ''' arms from the corners so the frame "focuses" in during open animations.
        ''' </summary>
        Public Shared Sub DrawViewfinder(g As Graphics, r As RectangleF, armLength As Single, pen As Pen, Optional t As Double = 1)
            Dim a = CSng(armLength * Easing.Clamp(t))
            If a < 0.5F Then Return
            ' top-left
            g.DrawLine(pen, r.Left, r.Top, r.Left + a, r.Top)
            g.DrawLine(pen, r.Left, r.Top, r.Left, r.Top + a)
            ' top-right
            g.DrawLine(pen, r.Right, r.Top, r.Right - a, r.Top)
            g.DrawLine(pen, r.Right, r.Top, r.Right, r.Top + a)
            ' bottom-left
            g.DrawLine(pen, r.Left, r.Bottom, r.Left + a, r.Bottom)
            g.DrawLine(pen, r.Left, r.Bottom, r.Left, r.Bottom - a)
            ' bottom-right
            g.DrawLine(pen, r.Right, r.Bottom, r.Right - a, r.Bottom)
            g.DrawLine(pen, r.Right, r.Bottom, r.Right, r.Bottom - a)
        End Sub

        ''' <summary>
        ''' Lens icon: a ring that sweeps closed (<paramref name="ringT"/>) and a glyph that draws itself in
        ''' (<paramref name="glyphT"/>). Both 0..1; pass 1 for a static icon.
        ''' </summary>
        Public Shared Sub DrawLensIcon(g As Graphics, center As PointF, diameter As Single, kind As IconKind,
                                       ringT As Double, glyphT As Double, color As Color, strokeWidth As Single,
                                       Optional filled As Boolean = False, Optional fillColor As Color = Nothing)
            Dim r = diameter / 2.0F
            Dim box As New RectangleF(center.X - r, center.Y - r, diameter, diameter)

            If filled AndAlso ringT > 0 Then
                Using b As New SolidBrush(Color.FromArgb(CInt(255 * Easing.Clamp(ringT)), fillColor))
                    g.FillEllipse(b, box)
                End Using
            End If

            Using ring As New Pen(color, strokeWidth) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                Dim sweep = CSng(360 * Easing.Clamp(ringT))
                If sweep >= 359.5F Then
                    g.DrawEllipse(ring, box)
                ElseIf sweep > 0.5F Then
                    g.DrawArc(ring, box, -90, sweep)
                End If
            End Using

            If glyphT <= 0 Then Return
            Dim gw = strokeWidth * 1.15F
            Using pen As New Pen(color, gw) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                Select Case kind
                    Case IconKind.Check
                        DrawPartialPolyline(g, pen, glyphT,
                            P(center, r, -0.34F, 0.02F), P(center, r, -0.1F, 0.26F), P(center, r, 0.36F, -0.24F))
                    Case IconKind.Cross
                        Dim first = Easing.Clamp(glyphT * 2)
                        Dim second = Easing.Clamp(glyphT * 2 - 1)
                        DrawPartialPolyline(g, pen, first, P(center, r, -0.26F, -0.26F), P(center, r, 0.26F, 0.26F))
                        DrawPartialPolyline(g, pen, second, P(center, r, 0.26F, -0.26F), P(center, r, -0.26F, 0.26F))
                    Case IconKind.Alert
                        DrawPartialPolyline(g, pen, glyphT, P(center, r, 0, -0.4F), P(center, r, 0, 0.1F))
                        If glyphT >= 0.85 Then DrawDot(g, color, P(center, r, 0, 0.36F), gw * 1.25F)
                    Case IconKind.Info
                        If glyphT >= 0.15 Then DrawDot(g, color, P(center, r, 0, -0.36F), gw * 1.25F)
                        DrawPartialPolyline(g, pen, glyphT, P(center, r, 0, -0.08F), P(center, r, 0, 0.4F))
                    Case IconKind.Question
                        DrawQuestion(g, center, r, color, glyphT)
                End Select
            End Using
        End Sub

        Private Shared Function P(c As PointF, r As Single, x As Single, y As Single) As PointF
            Return New PointF(c.X + x * r, c.Y + y * r)
        End Function

        Private Shared Sub DrawDot(g As Graphics, color As Color, c As PointF, diameter As Single)
            Using b As New SolidBrush(color)
                g.FillEllipse(b, c.X - diameter / 2, c.Y - diameter / 2, diameter, diameter)
            End Using
        End Sub

        Private Shared Sub DrawQuestion(g As Graphics, c As PointF, r As Single, color As Color, t As Double)
            Using path As New GraphicsPath()
                Using ff As New FontFamily(Theme.FontFamilySemibold)
                    Dim em = r * 1.25F
                    Dim fmt As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                    path.AddString("?", ff, CInt(FontStyle.Regular), em, New RectangleF(c.X - r, c.Y - r + r * 0.06F, r * 2, r * 2), fmt)
                End Using
                Dim alpha = CInt(255 * Easing.Clamp(t))
                Using b As New SolidBrush(Color.FromArgb(alpha, color))
                    ' Grow from the center so it reads as "appearing", like the other glyphs drawing in.
                    Dim s = CSng(0.6 + 0.4 * Easing.OutBack(t))
                    Using m As New Matrix()
                        m.Translate(c.X, c.Y)
                        m.Scale(s, s)
                        m.Translate(-c.X, -c.Y)
                        path.Transform(m)
                    End Using
                    g.FillPath(b, path)
                End Using
            End Using
        End Sub

        ''' <summary>Draws the first t (0..1) of a polyline's length, so strokes can animate in.</summary>
        Public Shared Sub DrawPartialPolyline(g As Graphics, pen As Pen, t As Double, ParamArray pts As PointF())
            t = Easing.Clamp(t)
            If t <= 0 OrElse pts.Length < 2 Then Return
            Dim lengths(pts.Length - 2) As Double
            Dim total As Double = 0
            For i = 0 To pts.Length - 2
                lengths(i) = Distance(pts(i), pts(i + 1))
                total += lengths(i)
            Next
            Dim remaining = total * t
            For i = 0 To pts.Length - 2
                If remaining <= 0 Then Exit For
                If remaining >= lengths(i) Then
                    g.DrawLine(pen, pts(i), pts(i + 1))
                    remaining -= lengths(i)
                Else
                    Dim f = CSng(remaining / lengths(i))
                    Dim endPt As New PointF(pts(i).X + (pts(i + 1).X - pts(i).X) * f, pts(i).Y + (pts(i + 1).Y - pts(i).Y) * f)
                    g.DrawLine(pen, pts(i), endPt)
                    remaining = 0
                End If
            Next
        End Sub

        Private Shared Function Distance(a As PointF, b As PointF) As Double
            Dim dx = b.X - a.X
            Dim dy = b.Y - a.Y
            Return Math.Sqrt(dx * dx + dy * dy)
        End Function

    End Class

End Namespace
