Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D

Namespace UI.Controls

    ''' <summary>
    ''' Preview frame for the logo / signature: the picture fitted inside viewfinder brackets on snow, or a dashed
    ''' empty frame with a hint. The control owns (and disposes) the Image it is given.
    ''' </summary>
    Public Class ImageSlot
        Inherits Control

        Private _image As Image
        Private _emptyText As String = "No picture yet"

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
        End Sub

        Public Property EmptyText As String
            Get
                Return _emptyText
            End Get
            Set(value As String)
                _emptyText = value
                Invalidate()
            End Set
        End Property

        Public Property Picture As Image
            Get
                Return _image
            End Get
            Set(value As Image)
                If _image IsNot Nothing AndAlso _image IsNot value Then _image.Dispose()
                _image = value
                Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim r As New RectangleF(0.5F, 0.5F, Width - 1.0F, Height - 1.0F)
            Using path = Gfx.RoundedRect(r, Theme.Dpi(CSng(Theme.RadiusSmall)))
                Using b As New SolidBrush(Theme.Snow)
                    g.FillPath(b, path)
                End Using
                If _image Is Nothing Then
                    Using p As New Pen(Theme.Silver, 1.0F) With {.DashStyle = DashStyle.Dash}
                        g.DrawPath(p, path)
                    End Using
                End If
            End Using
            If _image Is Nothing Then
                TextRenderer.DrawText(g, _emptyText, Theme.Label, Rectangle.Round(r), Theme.Slate,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                Return
            End If
            Dim pad = Theme.Dpi(16.0F)
            Dim area As New RectangleF(pad, pad, Width - pad * 2, Height - pad * 2)
            Dim scale = Math.Min(area.Width / _image.Width, area.Height / _image.Height)
            scale = Math.Min(scale, 1.0F * Theme.DpiScale)
            Dim w = _image.Width * scale
            Dim h = _image.Height * scale
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.DrawImage(_image, area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h)
            Using pen As New Pen(Theme.Silver, Theme.Dpi(1.5F)) With {.StartCap = LineCap.Square, .EndCap = LineCap.Square}
                Dim inset = Theme.Dpi(6.0F)
                Gfx.DrawViewfinder(g, New RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1), Theme.Dpi(12.0F), pen)
            End Using
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing AndAlso _image IsNot Nothing Then
                _image.Dispose()
                _image = Nothing
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
