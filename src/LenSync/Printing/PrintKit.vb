Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Printing

Namespace Printing

    ''' <summary>
    ''' Drawing helpers for printed documents. Everything is GDI+ (Graphics.DrawString) in points, so the same drawing
    ''' scales correctly on paper and on preview bitmaps.
    ''' </summary>
    Public NotInheritable Class PrintKit

        Private Sub New()
        End Sub

        ''' <summary>Single-line text clipped with an ellipsis to <paramref name="rect"/>.</summary>
        Public Shared Sub Text(g As Graphics, s As String, font As Font, color As Color, rect As RectangleF,
                               Optional align As StringAlignment = StringAlignment.Near)
            Using b As New SolidBrush(color)
                Using sf As New StringFormat(StringFormatFlags.NoWrap) With {.Alignment = align, .Trimming = StringTrimming.EllipsisCharacter}
                    g.DrawString(If(s, ""), font, b, rect, sf)
                End Using
            End Using
        End Sub

        ''' <summary>Wrapped text inside <paramref name="rect"/>.</summary>
        Public Shared Sub Paragraph(g As Graphics, s As String, font As Font, color As Color, rect As RectangleF)
            Using b As New SolidBrush(color)
                Using sf As New StringFormat() With {.Trimming = StringTrimming.EllipsisWord}
                    g.DrawString(If(s, ""), font, b, rect, sf)
                End Using
            End Using
        End Sub

        Public Shared Sub Rule(g As Graphics, x1 As Single, x2 As Single, y As Single, color As Color, thickness As Single)
            Using p As New Pen(color, thickness)
                g.DrawLine(p, x1, y, x2, y)
            End Using
        End Sub

        ''' <summary>Letter-spaced single-line text. Near: starts at x; Center: centered on x; Far: ends at x.</summary>
        Public Shared Sub Tracked(g As Graphics, s As String, font As Font, color As Color, x As Single, y As Single, tracking As Single,
                                  Optional align As StringAlignment = StringAlignment.Center)
            If String.IsNullOrEmpty(s) Then Return
            Using sf As StringFormat = CType(StringFormat.GenericTypographic.Clone(), StringFormat)
                ' Typographic measuring gives a space no width, so give it a word gap.
                Dim widths = s.Select(Function(c) If(c = " "c, font.SizeInPoints * 0.6F, g.MeasureString(c.ToString(), font, PointF.Empty, sf).Width)).ToArray()
                Dim total = widths.Sum() + tracking * (s.Length - 1)
                Dim cx = If(align = StringAlignment.Center, x - total / 2, If(align = StringAlignment.Far, x - total, x))
                Using b As New SolidBrush(color)
                    For i = 0 To s.Length - 1
                        g.DrawString(s(i).ToString(), font, b, cx, y, sf)
                        cx += widths(i) + tracking
                    Next
                End Using
            End Using
        End Sub

        ''' <summary>Loads a picture from the branding folder without locking it; Nothing if missing or unreadable.</summary>
        Public Shared Function LoadImage(path As String) As Image
            If String.IsNullOrEmpty(path) OrElse Not IO.File.Exists(path) Then Return Nothing
            Try
                Return Core.StudioAdmin.LoadImageCopy(path)
            Catch ex As Exception
                Core.AppLog.Write(ex, "Loading image for printing: " & path)
                Return Nothing
            End Try
        End Function

        ''' <summary>Fits <paramref name="img"/> inside maxW x maxH (points) at (x, y); returns the drawn size.</summary>
        Public Shared Function DrawFitted(g As Graphics, img As Image, x As Single, y As Single, maxW As Single, maxH As Single,
                                          Optional alignRight As Boolean = False) As SizeF
            Dim scale = Math.Min(maxW / img.Width, maxH / img.Height)
            Dim w = img.Width * scale
            Dim h = img.Height * scale
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.DrawImage(img, New RectangleF(If(alignRight, x + maxW - w, x), y, w, h))
            Return New SizeF(w, h)
        End Function

        ''' <summary>Puts the origin at the paper's corner (printers start at their hard margin) and switches to points.</summary>
        Public Shared Sub PreparePrinterGraphics(e As PrintPageEventArgs)
            Dim g = e.Graphics
            g.PageUnit = GraphicsUnit.Point
            g.TranslateTransform(-e.PageSettings.HardMarginX * 0.72F, -e.PageSettings.HardMarginY * 0.72F)
        End Sub

        ''' <summary>A bitmap <paramref name="widthPx"/> wide for a page of the given size in points, ready to draw in points.</summary>
        Public Shared Function NewPageBitmap(widthPx As Integer, pageWidthPt As Single, pageHeightPt As Single) As Bitmap
            widthPx = Math.Max(100, widthPx)
            Dim heightPx = CInt(Math.Round(widthPx * pageHeightPt / pageWidthPt))
            Dim bmp As New Bitmap(widthPx, heightPx)
            Dim dpi = widthPx / (pageWidthPt / 72.0F)
            bmp.SetResolution(dpi, dpi)
            Return bmp
        End Function

    End Class

End Namespace
