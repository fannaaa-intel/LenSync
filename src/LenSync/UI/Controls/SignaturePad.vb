Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

Namespace UI.Controls

    ''' <summary>
    ''' E-signature pad: sign with the mouse, touchpad, touch screen or pen (Windows turns pen/touch into mouse
    ''' input). Strokes are kept as points so they can be undone and redrawn at print resolution by
    ''' <see cref="ToImage"/>, which returns a transparent picture cropped to the ink.
    ''' </summary>
    Public Class SignaturePad
        Inherits Control

        Private ReadOnly _strokes As New List(Of List(Of PointF))
        Private _current As List(Of PointF)

        ''' <summary>Raised when a stroke starts, is undone or the pad is cleared.</summary>
        Public Event InkChanged As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw, True)
            Cursor = Cursors.Cross
            TabStop = False
            AccessibleName = "Signature pad"
            AccessibleDescription = "Draw the signature with the mouse, touchpad or pen."
            AccessibleRole = AccessibleRole.Graphic
        End Sub

        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return _strokes.Count = 0
            End Get
        End Property

        Public Sub Clear()
            If _strokes.Count = 0 Then Return
            _strokes.Clear()
            _current = Nothing
            Invalidate()
            RaiseEvent InkChanged(Me, EventArgs.Empty)
        End Sub

        Public Sub Undo()
            If _strokes.Count = 0 Then Return
            _strokes.RemoveAt(_strokes.Count - 1)
            _current = Nothing
            Invalidate()
            RaiseEvent InkChanged(Me, EventArgs.Empty)
        End Sub

        ' ---------------------------------------------------------------- input
        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Button <> MouseButtons.Left Then Return
            _current = New List(Of PointF) From {New PointF(e.X, e.Y)}
            _strokes.Add(_current)
            Capture = True
            Invalidate()
            RaiseEvent InkChanged(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If _current Is Nothing Then Return
            ' Keep the pen inside the pad; skip sub-pixel jitter so curves stay smooth.
            Dim p As New PointF(Math.Max(0, Math.Min(Width - 1, e.X)), Math.Max(0, Math.Min(Height - 1, e.Y)))
            Dim last = _current(_current.Count - 1)
            If Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) < 1.5F Then Return
            _current.Add(p)
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            _current = Nothing
            Capture = False
        End Sub

        Protected Overrides Sub OnMouseCaptureChanged(e As EventArgs)
            MyBase.OnMouseCaptureChanged(e)
            _current = Nothing
        End Sub

        ' ---------------------------------------------------------------- drawing
        Private ReadOnly Property InkWidth As Single
            Get
                Return Theme.Dpi(2.4F)
            End Get
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim r As New RectangleF(0.5F, 0.5F, Width - 1.0F, Height - 1.0F)
            Using path = Gfx.RoundedRect(r, Theme.Dpi(CSng(Theme.RadiusSmall)))
                Using b As New SolidBrush(Theme.White)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(If(Focused OrElse Capture, Theme.Charcoal, Theme.Silver), 1.0F)
                    g.DrawPath(p, path)
                End Using
            End Using

            ' Guide: an "x" and a line to sign on, like a paper form. Not part of the saved signature.
            Dim side = Theme.Dpi(28.0F)
            Dim baseY = Height - Theme.Dpi(44.0F)
            Using guide As New Pen(Theme.Mist, Theme.Dpi(1.0F))
                g.DrawLine(guide, side, baseY, Width - side, baseY)
            End Using
            TextRenderer.DrawText(g, "×", Theme.Body, New Point(CInt(side) - Theme.Dpi(2), CInt(baseY) - Theme.Body.Height - Theme.Dpi(2)), Theme.Silver)
            If IsEmpty Then
                TextRenderer.DrawText(g, "Sign here", Theme.Label, New Rectangle(0, CInt(baseY) + Theme.Dpi(8), Width, Theme.Label.Height + Theme.Dpi(4)),
                                      Theme.Silver, TextFormatFlags.HorizontalCenter)
            End If

            DrawStrokes(g, _strokes, InkWidth, Theme.Ink, 1.0F, PointF.Empty)
        End Sub

        Private Shared Sub DrawStrokes(g As Graphics, strokes As IEnumerable(Of List(Of PointF)), width As Single, color As Color,
                                       scale As Single, origin As PointF)
            Using pen As New Pen(color, width) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                Using dot As New SolidBrush(color)
                    For Each s In strokes
                        Dim pts = s.Select(Function(p) New PointF((p.X - origin.X) * scale, (p.Y - origin.Y) * scale)).ToArray()
                        If pts.Length = 1 Then
                            ' A tap leaves a dot (e.g. the dot of an "i").
                            g.FillEllipse(dot, pts(0).X - width / 2, pts(0).Y - width / 2, width, width)
                        ElseIf pts.Length = 2 Then
                            g.DrawLine(pen, pts(0), pts(1))
                        Else
                            g.DrawCurve(pen, pts, 0.5F)
                        End If
                    Next
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' The signature as a transparent picture, cropped to the ink with a small margin and drawn larger than the
        ''' screen (up to <paramref name="maxWidth"/> x <paramref name="maxHeight"/>) so it stays sharp on paper.
        ''' Returns Nothing if the pad is empty.
        ''' </summary>
        Public Function ToImage(Optional maxWidth As Integer = 600, Optional maxHeight As Integer = 240) As Bitmap
            If IsEmpty Then Return Nothing
            Dim all = _strokes.SelectMany(Function(s) s).ToList()
            Dim margin = InkWidth * 2
            Dim left = all.Min(Function(p) p.X) - margin
            Dim top = all.Min(Function(p) p.Y) - margin
            Dim w = Math.Max(1.0F, all.Max(Function(p) p.X) + margin - left)
            Dim h = Math.Max(1.0F, all.Max(Function(p) p.Y) + margin - top)
            Dim scale = CSng(Math.Min(3.0, Math.Min(maxWidth / w, maxHeight / h)))
            Dim bmp As New Bitmap(Math.Max(16, CInt(Math.Ceiling(w * scale))), Math.Max(16, CInt(Math.Ceiling(h * scale))), PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(bmp)
                Gfx.HighQuality(g)
                g.Clear(Color.Transparent)
                DrawStrokes(g, _strokes, InkWidth * scale, Theme.Ink, scale, New PointF(left, top))
            End Using
            Return bmp
        End Function

    End Class

End Namespace
