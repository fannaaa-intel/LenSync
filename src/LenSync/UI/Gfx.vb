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

    ''' <summary>Line icons for navigation and inputs (drawn, so they stay crisp at any DPI).</summary>
    Public Enum LineIcon
        Dashboard
        Bookings
        AddService
        Reports
        Settings
        SignOut
        Key
        Eye
        EyeOff
        User
        Lock
        Search
        Clock
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

        ''' <summary>Letter-spaced single-line text. <paramref name="align"/>: Near = starts at x, Center = centered on x.</summary>
        Public Shared Sub DrawTracked(g As Graphics, text As String, font As Font, color As Color,
                                      x As Single, top As Integer, tracking As Single,
                                      Optional align As StringAlignment = StringAlignment.Near)
            If String.IsNullOrEmpty(text) Then Return
            Const flags As TextFormatFlags = TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine
            Dim widths(text.Length - 1) As Integer
            Dim total As Single = 0
            For i = 0 To text.Length - 1
                widths(i) = TextRenderer.MeasureText(g, text(i).ToString(), font, New Size(Integer.MaxValue, Integer.MaxValue), flags).Width
                total += widths(i)
            Next
            total += tracking * (text.Length - 1)
            Dim cx = If(align = StringAlignment.Center, x - total / 2, If(align = StringAlignment.Far, x - total, x))
            For i = 0 To text.Length - 1
                TextRenderer.DrawText(g, text(i).ToString(), font, New Point(CInt(cx), top), color, flags)
                cx += widths(i) + tracking
            Next
        End Sub

        ''' <summary>
        ''' Width DrawTracked will use for <paramref name="text"/>. Needs the Graphics: the overload without one
        ''' ignores NoPadding and reports about double the width of a single character.
        ''' </summary>
        Public Shared Function MeasureTracked(g As Graphics, text As String, font As Font, tracking As Single) As Integer
            If String.IsNullOrEmpty(text) Then Return 0
            Const flags As TextFormatFlags = TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine
            Dim total As Single = 0
            For Each ch In text
                total += TextRenderer.MeasureText(g, ch.ToString(), font, New Size(Integer.MaxValue, Integer.MaxValue), flags).Width
            Next
            Return CInt(total + tracking * (text.Length - 1))
        End Function

        ''' <summary>LENSYNC mark: a lens (ring + inner ring) inside viewfinder brackets.</summary>
        Public Shared Sub DrawBrandMark(g As Graphics, r As RectangleF, color As Color, stroke As Single)
            Using pen As New Pen(color, stroke) With {.StartCap = LineCap.Square, .EndCap = LineCap.Square}
                DrawViewfinder(g, r, r.Width * 0.28F, pen)
                Dim c As New PointF(r.X + r.Width / 2, r.Y + r.Height / 2)
                Dim d1 = r.Width * 0.52F
                Dim d2 = r.Width * 0.24F
                g.DrawEllipse(pen, c.X - d1 / 2, c.Y - d1 / 2, d1, d1)
                g.DrawEllipse(pen, c.X - d2 / 2, c.Y - d2 / 2, d2, d2)
            End Using
        End Sub

        ''' <summary>Draws a line icon fitted into the square <paramref name="r"/>.</summary>
        Public Shared Sub DrawLineIcon(g As Graphics, icon As LineIcon, r As RectangleF, color As Color, stroke As Single)
            Dim s = r.Width
            Dim x = r.X
            Dim y = r.Y
            Using pen As New Pen(color, stroke) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                Select Case icon
                    Case LineIcon.Dashboard
                        Dim q = s * 0.42F
                        Dim gap = s * 0.16F
                        For Each pt In {New PointF(x, y), New PointF(x + q + gap, y), New PointF(x, y + q + gap), New PointF(x + q + gap, y + q + gap)}
                            Using p = RoundedRect(New RectangleF(pt.X, pt.Y, q, q), s * 0.08F)
                                g.DrawPath(pen, p)
                            End Using
                        Next
                    Case LineIcon.Bookings
                        Using p = RoundedRect(New RectangleF(x, y + s * 0.12F, s, s * 0.86F), s * 0.1F)
                            g.DrawPath(pen, p)
                        End Using
                        g.DrawLine(pen, x, y + s * 0.38F, x + s, y + s * 0.38F)
                        g.DrawLine(pen, x + s * 0.28F, y, x + s * 0.28F, y + s * 0.22F)
                        g.DrawLine(pen, x + s * 0.72F, y, x + s * 0.72F, y + s * 0.22F)
                        Using b As New SolidBrush(color)
                            Dim d = stroke * 1.1F
                            For Each px In {0.3F, 0.5F, 0.7F}
                                g.FillEllipse(b, x + s * px - d / 2, y + s * 0.62F - d / 2, d, d)
                            Next
                        End Using
                    Case LineIcon.AddService
                        ' Receipt with a torn edge and a plus.
                        Dim pts = {New PointF(x + s * 0.1F, y), New PointF(x + s * 0.9F, y), New PointF(x + s * 0.9F, y + s),
                                   New PointF(x + s * 0.74F, y + s * 0.88F), New PointF(x + s * 0.58F, y + s),
                                   New PointF(x + s * 0.42F, y + s * 0.88F), New PointF(x + s * 0.26F, y + s),
                                   New PointF(x + s * 0.1F, y + s * 0.88F)}
                        g.DrawPolygon(pen, pts)
                        g.DrawLine(pen, x + s * 0.5F, y + s * 0.24F, x + s * 0.5F, y + s * 0.62F)
                        g.DrawLine(pen, x + s * 0.31F, y + s * 0.43F, x + s * 0.69F, y + s * 0.43F)
                    Case LineIcon.Reports
                        g.DrawLine(pen, x, y + s, x + s, y + s)
                        g.DrawLine(pen, x + s * 0.2F, y + s * 0.82F, x + s * 0.2F, y + s * 0.5F)
                        g.DrawLine(pen, x + s * 0.5F, y + s * 0.82F, x + s * 0.5F, y + s * 0.12F)
                        g.DrawLine(pen, x + s * 0.8F, y + s * 0.82F, x + s * 0.8F, y + s * 0.34F)
                    Case LineIcon.Settings
                        ' Three sliders.
                        Dim knob = s * 0.24F
                        Dim rows = {New Tuple(Of Single, Single)(0.15F, 0.3F), New Tuple(Of Single, Single)(0.5F, 0.7F), New Tuple(Of Single, Single)(0.85F, 0.42F)}
                        For Each row In rows
                            Dim ly = y + s * row.Item1
                            Dim kx = x + s * row.Item2
                            g.DrawLine(pen, x, ly, kx - knob / 2, ly)
                            g.DrawLine(pen, kx + knob / 2, ly, x + s, ly)
                            g.DrawEllipse(pen, kx - knob / 2, ly - knob / 2, knob, knob)
                        Next
                    Case LineIcon.SignOut
                        g.DrawLines(pen, {New PointF(x + s * 0.45F, y), New PointF(x, y), New PointF(x, y + s), New PointF(x + s * 0.45F, y + s)})
                        g.DrawLine(pen, x + s * 0.32F, y + s * 0.5F, x + s, y + s * 0.5F)
                        g.DrawLines(pen, {New PointF(x + s * 0.76F, y + s * 0.26F), New PointF(x + s, y + s * 0.5F), New PointF(x + s * 0.76F, y + s * 0.74F)})
                    Case LineIcon.Key
                        Dim d = s * 0.44F
                        g.DrawEllipse(pen, x, y + s * 0.28F, d, d)
                        g.DrawLine(pen, x + d, y + s * 0.5F, x + s, y + s * 0.5F)
                        g.DrawLine(pen, x + s * 0.82F, y + s * 0.5F, x + s * 0.82F, y + s * 0.7F)
                        g.DrawLine(pen, x + s * 0.66F, y + s * 0.5F, x + s * 0.66F, y + s * 0.64F)
                    Case LineIcon.Eye, LineIcon.EyeOff
                        Using p As New GraphicsPath()
                            p.AddBezier(x, y + s * 0.5F, x + s * 0.25F, y + s * 0.12F, x + s * 0.75F, y + s * 0.12F, x + s, y + s * 0.5F)
                            p.AddBezier(x + s, y + s * 0.5F, x + s * 0.75F, y + s * 0.88F, x + s * 0.25F, y + s * 0.88F, x, y + s * 0.5F)
                            g.DrawPath(pen, p)
                        End Using
                        Dim d = s * 0.28F
                        g.DrawEllipse(pen, x + s / 2 - d / 2, y + s / 2 - d / 2, d, d)
                        If icon = LineIcon.EyeOff Then g.DrawLine(pen, x + s * 0.12F, y + s * 0.9F, x + s * 0.88F, y + s * 0.1F)
                    Case LineIcon.User
                        Dim d = s * 0.42F
                        g.DrawEllipse(pen, x + s / 2 - d / 2, y, d, d)
                        g.DrawArc(pen, x + s * 0.06F, y + s * 0.52F, s * 0.88F, s * 0.9F, 180, 180)
                    Case LineIcon.Lock
                        Using p = RoundedRect(New RectangleF(x + s * 0.08F, y + s * 0.44F, s * 0.84F, s * 0.56F), s * 0.1F)
                            g.DrawPath(pen, p)
                        End Using
                        g.DrawArc(pen, x + s * 0.26F, y, s * 0.48F, s * 0.6F, 180, 180)
                        g.DrawLine(pen, x + s * 0.26F, y + s * 0.3F, x + s * 0.26F, y + s * 0.44F)
                        g.DrawLine(pen, x + s * 0.74F, y + s * 0.3F, x + s * 0.74F, y + s * 0.44F)
                    Case LineIcon.Search
                        Dim d = s * 0.66F
                        g.DrawEllipse(pen, x, y, d, d)
                        g.DrawLine(pen, x + d * 0.86F, y + d * 0.86F, x + s, y + s)
                    Case LineIcon.Clock
                        g.DrawEllipse(pen, x, y, s, s)
                        g.DrawLines(pen, {New PointF(x + s * 0.5F, y + s * 0.22F), New PointF(x + s * 0.5F, y + s * 0.5F), New PointF(x + s * 0.7F, y + s * 0.64F)})
                End Select
            End Using
        End Sub

        ''' <summary>A small V (down) or ^ (up) chevron centered in <paramref name="r"/>.</summary>
        Public Shared Sub DrawChevron(g As Graphics, r As RectangleF, color As Color, stroke As Single, Optional up As Boolean = False,
                                      Optional horizontal As Integer = 0)
            Dim w = Math.Min(r.Width, r.Height) * 0.5F
            Dim cx = r.X + r.Width / 2
            Dim cy = r.Y + r.Height / 2
            Using pen As New Pen(color, stroke) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                If horizontal <> 0 Then
                    ' horizontal: -1 = "<", 1 = ">"
                    Dim dx = w * 0.28F * horizontal
                    g.DrawLines(pen, {New PointF(cx - dx, cy - w / 2), New PointF(cx + dx, cy), New PointF(cx - dx, cy + w / 2)})
                Else
                    Dim dy = w * 0.28F * If(up, -1, 1)
                    g.DrawLines(pen, {New PointF(cx - w / 2, cy - dy), New PointF(cx, cy + dy), New PointF(cx + w / 2, cy - dy)})
                End If
            End Using
        End Sub

        Private Shared Function Distance(a As PointF, b As PointF) As Double
            Dim dx = b.X - a.X
            Dim dy = b.Y - a.Y
            Return Math.Sqrt(dx * dx + dy * dy)
        End Function

    End Class

End Namespace
