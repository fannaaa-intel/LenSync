Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

Namespace UI.Controls

    Public Enum PreviewZoom
        ''' <summary>The page fills the width (up to real paper size). The default.</summary>
        FitWidth
        ''' <summary>The whole page is visible.</summary>
        WholePage
        ''' <summary>A fixed percentage of real paper size (ZoomPercent).</summary>
        Fixed
    End Enum

    ''' <summary>
    ''' On-screen preview of a printed document, one page at a time. Pages are drawn by the caller (the same drawing
    ''' that prints) and cached at the current size. No visible scrollbars: the wheel or Up/Down scroll, a quiet tag says
    ''' when more is below, PageUp/PageDown change page.
    ''' Zoom: the "−  Fit width  +" pill at the top right (the middle toggles Fit width / Whole page), Ctrl+wheel (zooms
    ''' toward the mouse), Ctrl+Plus / Ctrl+Minus / Ctrl+0. Zoomed in, drag the page or use Shift+wheel / Left / Right to
    ''' move around. With no document it shows a message inside viewfinder brackets.
    ''' </summary>
    Public Class PagePreview
        Inherits Control

        Private Shared ReadOnly Steps As Double() = {0.25, 0.33, 0.5, 0.67, 0.75, 1.0, 1.25, 1.5, 2.0}
        Private Const MinZoom As Double = 0.25
        Private Const MaxZoom As Double = 2.0
        Private Const ZoomMs As Integer = 140

        Private _render As Func(Of Integer, Integer, Bitmap)
        Private _pageCount As Integer
        Private _aspect As Single = 11.0F / 8.5F
        Private _index As Integer
        Private _bitmap As Bitmap
        Private _bitmapKey As String = ""
        Private _scroll As Integer
        Private _scrollFrom As Integer
        Private _scrollTo As Integer
        Private _scrollX As Integer
        Private _message As String = ""
        Private _busy As Boolean
        Private _failed As Boolean
        Private _pageT As Double = 1
        Private _direction As Integer
        Private ReadOnly _scrollTween As New Tween()
        Private ReadOnly _pageTween As New Tween()

        ' Zoom
        Private _zoomMode As PreviewZoom = PreviewZoom.FitWidth
        Private _zoom As Double = 1
        Private ReadOnly _zoomTween As New Tween()
        Private _zoomT As Double = 1
        Private _zoomFromW As Integer

        ' Mouse: pill segments (0 = zoom out, 1 = fit toggle, 2 = zoom in) and drag-to-move.
        Private _hoverSeg As Integer = -1
        Private _pressedSeg As Integer = -1
        Private _dragging As Boolean
        Private _dragStart As Point
        Private _dragScroll As Point
        Private ReadOnly _tip As New ToolTip() With {.InitialDelay = 500, .ShowAlways = True}

        Public Event PageChanged As EventHandler
        Public Event ZoomChanged As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            TabStop = True
            BackColor = Theme.Snow
            AccessibleRole = AccessibleRole.Graphic
            AccessibleName = "Print preview"
            UpdateAccessibleText()
        End Sub

        ' ---------------------------------------------------------------- document
        ''' <summary>
        ''' Shows a document of <paramref name="pageCount"/> pages. <paramref name="render"/>(index, widthPx) draws one
        ''' page; <paramref name="aspect"/> = page height / width. Keeps the zoom; keeps the page and position when
        ''' <paramref name="keepPage"/> and the page still exists.
        ''' </summary>
        Public Sub SetDocument(pageCount As Integer, aspect As Single, render As Func(Of Integer, Integer, Bitmap), Optional keepPage As Boolean = False)
            _render = render
            _pageCount = Math.Max(0, pageCount)
            _aspect = aspect
            _message = ""
            _busy = False
            Dim newIndex = If(keepPage, Math.Min(_index, Math.Max(0, _pageCount - 1)), 0)
            If newIndex <> _index OrElse Not keepPage Then
                _scroll = 0
                _scrollTo = 0
                _scrollX = 0
            End If
            _index = newIndex
            ClampScroll()
            DropBitmap()
            UpdateAccessibleText()
            Invalidate()
            RaiseEvent PageChanged(Me, EventArgs.Empty)
        End Sub

        ''' <summary>Clears the document and shows <paramref name="message"/> instead (e.g. "Fix the dates...").</summary>
        Public Sub ShowMessage(message As String)
            _render = Nothing
            _pageCount = 0
            _index = 0
            _scroll = 0
            _scrollTo = 0
            _scrollX = 0
            _message = If(message, "")
            _busy = False
            DropBitmap()
            AccessibleDescription = _message
            Invalidate()
            RaiseEvent PageChanged(Me, EventArgs.Empty)
        End Sub

        ''' <summary>True while a new version is being prepared: the old page stays, with an "Updating" tag.</summary>
        Public Property Busy As Boolean
            Get
                Return _busy
            End Get
            Set(value As Boolean)
                If _busy = value Then Return
                _busy = value
                Invalidate()
            End Set
        End Property

        ''' <summary>Redraws the pages (e.g. after the document was issued and its footer changed).</summary>
        Public Sub RefreshPages()
            DropBitmap()
            Invalidate()
        End Sub

        Public ReadOnly Property PageCount As Integer
            Get
                Return _pageCount
            End Get
        End Property

        Public Property PageIndex As Integer
            Get
                Return _index
            End Get
            Set(value As Integer)
                If _pageCount = 0 Then Return
                value = Math.Max(0, Math.Min(_pageCount - 1, value))
                If value = _index Then Return
                _direction = Math.Sign(value - _index)
                _index = value
                _scrollTween.Stop()
                _scroll = 0
                _scrollTo = 0
                DropBitmap()
                UpdateAccessibleText()
                _pageTween.Start(Theme.OpenMs, Sub(t)
                                                   _pageT = Easing.OutCubic(t)
                                                   Invalidate()
                                               End Sub)
                RaiseEvent PageChanged(Me, EventArgs.Empty)
            End Set
        End Property

        ' ---------------------------------------------------------------- zoom
        Public ReadOnly Property ZoomMode As PreviewZoom
            Get
                Return _zoomMode
            End Get
        End Property

        ''' <summary>Current zoom as a percentage of real paper size (100 = 8.5" shown as 8.5" at this screen's DPI).</summary>
        Public ReadOnly Property ZoomPercent As Integer
            Get
                Return CInt(Math.Round(EffectiveZoom * 100))
            End Get
        End Property

        ''' <summary>What the pill shows: "Fit width", "Whole page" or "125%".</summary>
        Public ReadOnly Property ZoomText As String
            Get
                Select Case _zoomMode
                    Case PreviewZoom.FitWidth : Return "Fit width"
                    Case PreviewZoom.WholePage : Return "Whole page"
                    Case Else : Return $"{ZoomPercent}%"
                End Select
            End Get
        End Property

        Public ReadOnly Property CanZoomIn As Boolean
            Get
                Return NextZoom(1) IsNot Nothing
            End Get
        End Property

        Public ReadOnly Property CanZoomOut As Boolean
            Get
                Return NextZoom(-1) IsNot Nothing
            End Get
        End Property

        Public Sub ZoomIn(Optional anchor As Point? = Nothing)
            StepZoom(1, anchor)
        End Sub

        Public Sub ZoomOut(Optional anchor As Point? = Nothing)
            StepZoom(-1, anchor)
        End Sub

        Public Sub SetZoomMode(mode As PreviewZoom)
            ApplyZoom(mode, _zoom, Nothing)
        End Sub

        ''' <summary>Real paper width at this screen's DPI (a letter page is 8.5" = 816 px at 96 dpi).</summary>
        Private ReadOnly Property PaperWidthPx As Integer
            Get
                Return Theme.Dpi(816)
            End Get
        End Property

        Private Function ZoomFor(mode As PreviewZoom) As Double
            Dim availW = Math.Max(1, Width - Margin96 * 2)
            Dim availH = Math.Max(1, Height - Margin96 * 2)
            Select Case mode
                Case PreviewZoom.FitWidth
                    Return Math.Max(MinZoom, Math.Min(1.0, availW / PaperWidthPx))
                Case PreviewZoom.WholePage
                    Return Math.Max(MinZoom, Math.Min(1.0, Math.Min(availW / PaperWidthPx, availH / (PaperWidthPx * _aspect))))
                Case Else
                    Return _zoom
            End Select
        End Function

        Private ReadOnly Property EffectiveZoom As Double
            Get
                Return ZoomFor(_zoomMode)
            End Get
        End Property

        ''' <summary>The next zoom up (+1) or down (-1): the fixed steps plus "fit width" and "whole page".</summary>
        Private Function NextZoom(direction As Integer) As Tuple(Of PreviewZoom, Double)
            Dim current = EffectiveZoom
            Dim fitW = ZoomFor(PreviewZoom.FitWidth)
            Dim fitP = ZoomFor(PreviewZoom.WholePage)
            Dim candidates As New List(Of Tuple(Of PreviewZoom, Double)) From {Tuple.Create(PreviewZoom.FitWidth, fitW)}
            If Math.Abs(fitP - fitW) >= 0.01 Then candidates.Add(Tuple.Create(PreviewZoom.WholePage, fitP))
            ' A fixed step at (almost) a fit size is left out, so the pill says "Fit width" rather than "75%" for it.
            For Each s In Steps
                If Math.Abs(s - fitW) >= 0.02 AndAlso Math.Abs(s - fitP) >= 0.02 Then candidates.Add(Tuple.Create(PreviewZoom.Fixed, s))
            Next
            ' Always move strictly past the current size, so a step never "lands" where it already is.
            If direction > 0 Then
                Return candidates.Where(Function(c) c.Item2 > current + 0.005).OrderBy(Function(c) c.Item2).FirstOrDefault()
            End If
            Return candidates.Where(Function(c) c.Item2 < current - 0.005).OrderByDescending(Function(c) c.Item2).FirstOrDefault()
        End Function

        Private Sub StepZoom(direction As Integer, anchor As Point?)
            Dim target = NextZoom(direction)
            If target Is Nothing Then Return
            ApplyZoom(target.Item1, target.Item2, anchor)
        End Sub

        ''' <summary>Changes the zoom keeping the point under <paramref name="anchor"/> (default: the centre) in place.</summary>
        Private Sub ApplyZoom(mode As PreviewZoom, value As Double, anchor As Point?)
            Dim a = If(anchor, New Point(Width \ 2, Height \ 2))
            Dim oldW = DisplayW
            Dim oldH = DisplayH
            Dim fx = If(oldW > 0, Easing.Clamp((a.X - PageLeft(oldW)) / CDbl(oldW)), 0.5)
            Dim fy = If(oldH > 0, Easing.Clamp((a.Y - PageTop) / CDbl(oldH)), 0)
            _zoomMode = mode
            If mode = PreviewZoom.Fixed Then _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, value))
            UpdateAccessibleText()
            RaiseEvent ZoomChanged(Me, EventArgs.Empty)
            If _pageCount = 0 OrElse TargetW = oldW Then
                ClampScroll()
                Invalidate()
                Return
            End If
            _scrollTween.Stop()
            _zoomFromW = oldW
            If Not IsHandleCreated Then
                _zoomT = 1
                KeepAnchor(fx, fy, a)
                Invalidate()
                Return
            End If
            ' Grow the current picture (cheap), then redraw it sharp at the new size when the animation ends.
            _zoomTween.Start(ZoomMs,
                Sub(t)
                    _zoomT = t
                    KeepAnchor(fx, fy, a)
                    Invalidate()
                End Sub,
                Sub()
                    _zoomT = 1
                    KeepAnchor(fx, fy, a)
                    Invalidate()
                End Sub)
        End Sub

        Private Sub KeepAnchor(fx As Double, fy As Double, a As Point)
            Dim w = DisplayW
            Dim h = DisplayH
            _scrollX = If(MaxScrollX > 0, CInt(Margin96 + fx * w - a.X), 0)
            _scroll = CInt(Margin96 + fy * h - a.Y)
            ClampScroll()
        End Sub

        ' ---------------------------------------------------------------- geometry
        Private ReadOnly Property Margin96 As Integer
            Get
                Return Theme.Dpi(18)
            End Get
        End Property

        ''' <summary>Page width at the chosen zoom.</summary>
        Private ReadOnly Property TargetW As Integer
            Get
                Return Math.Max(Theme.Dpi(60), CInt(Math.Round(PaperWidthPx * EffectiveZoom)))
            End Get
        End Property

        ''' <summary>Page width on screen right now (between the old and new size while a zoom animates).</summary>
        Private ReadOnly Property DisplayW As Integer
            Get
                If _zoomT >= 1 OrElse _zoomFromW <= 0 Then Return TargetW
                Return CInt(_zoomFromW + (TargetW - _zoomFromW) * Easing.OutCubic(_zoomT))
            End Get
        End Property

        Private ReadOnly Property DisplayH As Integer
            Get
                Return CInt(Math.Round(DisplayW * _aspect))
            End Get
        End Property

        Private Function PageLeft(w As Integer) As Integer
            If w + Margin96 * 2 <= Width Then Return (Width - w) \ 2
            Return Margin96 - _scrollX
        End Function

        Private ReadOnly Property PageTop As Integer
            Get
                Return Margin96 - _scroll
            End Get
        End Property

        Private ReadOnly Property MaxScroll As Integer
            Get
                Return Math.Max(0, DisplayH + Margin96 * 2 - Height)
            End Get
        End Property

        Private ReadOnly Property MaxScrollX As Integer
            Get
                Return Math.Max(0, DisplayW + Margin96 * 2 - Width)
            End Get
        End Property

        Private ReadOnly Property CanMove As Boolean
            Get
                Return _pageCount > 0 AndAlso (MaxScroll > 0 OrElse MaxScrollX > 0)
            End Get
        End Property

        Private Sub ClampScroll()
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll))
            _scrollTo = Math.Max(0, Math.Min(MaxScroll, _scrollTo))
            _scrollX = Math.Max(0, Math.Min(MaxScrollX, _scrollX))
        End Sub

        Private Sub ScrollBy(delta As Integer)
            ScrollTo(_scrollTo + delta)
        End Sub

        Private Sub ScrollTo(target As Integer)
            target = Math.Max(0, Math.Min(MaxScroll, target))
            If target = _scrollTo AndAlso Not _scrollTween.IsRunning Then Return
            _scrollFrom = _scroll
            _scrollTo = target
            _scrollTween.Start(Theme.HoverMs + 40, Sub(t)
                                                       _scroll = CInt(_scrollFrom + (_scrollTo - _scrollFrom) * Easing.OutCubic(t))
                                                       Invalidate()
                                                   End Sub)
        End Sub

        Private Sub ScrollXBy(delta As Integer)
            _scrollX = Math.Max(0, Math.Min(MaxScrollX, _scrollX + delta))
            Invalidate()
        End Sub

        ' ---------------------------------------------------------------- zoom pill
        Private ReadOnly Property ShowPill As Boolean
            Get
                Return _pageCount > 0 AndAlso Width > PillBounds.Width + Theme.Dpi(40)
            End Get
        End Property

        Private ReadOnly Property PillBounds As Rectangle
            Get
                Dim w = Theme.Dpi(34) * 2 + Theme.Dpi(88)
                Dim h = Theme.Dpi(32)
                Return New Rectangle(Width - w - Theme.Dpi(12), Theme.Dpi(10), w, h)
            End Get
        End Property

        Private Function SegmentBounds(index As Integer) As Rectangle
            Dim p = PillBounds
            Dim side = Theme.Dpi(34)
            Select Case index
                Case 0 : Return New Rectangle(p.X, p.Y, side, p.Height)
                Case 2 : Return New Rectangle(p.Right - side, p.Y, side, p.Height)
                Case Else : Return New Rectangle(p.X + side, p.Y, p.Width - side * 2, p.Height)
            End Select
        End Function

        Private Function SegmentAt(pt As Point) As Integer
            If Not ShowPill OrElse Not PillBounds.Contains(pt) Then Return -1
            For i = 0 To 2
                If SegmentBounds(i).Contains(pt) Then Return i
            Next
            Return -1
        End Function

        Private Function SegmentEnabled(index As Integer) As Boolean
            Select Case index
                Case 0 : Return CanZoomOut
                Case 2 : Return CanZoomIn
                Case Else : Return True
            End Select
        End Function

        Private Sub ActivateSegment(index As Integer)
            Select Case index
                Case 0 : ZoomOut()
                Case 2 : ZoomIn()
                Case 1 : SetZoomMode(If(_zoomMode = PreviewZoom.FitWidth, PreviewZoom.WholePage, PreviewZoom.FitWidth))
            End Select
        End Sub

        Private Sub SetHoverSegment(index As Integer)
            If index = _hoverSeg Then Return
            _hoverSeg = index
            Select Case index
                Case 0 : _tip.SetToolTip(Me, "Zoom out (Ctrl+Minus)")
                Case 2 : _tip.SetToolTip(Me, "Zoom in (Ctrl+Plus, or Ctrl+mouse wheel)")
                Case 1 : _tip.SetToolTip(Me, If(_zoomMode = PreviewZoom.FitWidth, "Show the whole page", "Fit the page to the width (Ctrl+0)"))
                Case Else : _tip.SetToolTip(Me, "")
            End Select
            Invalidate(PillBounds)
        End Sub

        Private Sub UpdateAccessibleText()
            AccessibleDescription = If(_pageCount = 0, _message,
                $"Page {_index + 1} of {_pageCount}, zoom {ZoomText}. Ctrl+Plus and Ctrl+Minus zoom, Ctrl+0 fits the width.")
        End Sub

        ' ---------------------------------------------------------------- input
        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            If e.Button <> MouseButtons.Left Then Return
            Dim seg = SegmentAt(e.Location)
            If seg >= 0 Then
                _pressedSeg = seg
                Return
            End If
            If CanMove Then
                _dragging = True
                _dragStart = e.Location
                _dragScroll = New Point(_scrollX, _scroll)
                _scrollTween.Stop()
                Cursor = Cursors.SizeAll
            End If
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If _dragging Then
                _scrollX = _dragScroll.X - (e.X - _dragStart.X)
                _scroll = _dragScroll.Y - (e.Y - _dragStart.Y)
                _scrollTo = _scroll
                ClampScroll()
                _scrollTo = _scroll
                Invalidate()
                Return
            End If
            Dim seg = SegmentAt(e.Location)
            SetHoverSegment(seg)
            Cursor = If(seg >= 0, If(SegmentEnabled(seg), Cursors.Hand, Cursors.Default), If(CanMove, Cursors.SizeAll, Cursors.Default))
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If _dragging Then
                _dragging = False
                Cursor = If(CanMove, Cursors.SizeAll, Cursors.Default)
                Return
            End If
            Dim seg = SegmentAt(e.Location)
            If e.Button = MouseButtons.Left AndAlso seg >= 0 AndAlso seg = _pressedSeg AndAlso SegmentEnabled(seg) Then ActivateSegment(seg)
            _pressedSeg = -1
            Invalidate(PillBounds)
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            SetHoverSegment(-1)
        End Sub

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            MyBase.OnMouseWheel(e)
            If _pageCount = 0 Then Return
            If (ModifierKeys And Keys.Control) = Keys.Control Then
                If e.Delta > 0 Then ZoomIn(e.Location) Else ZoomOut(e.Location)
            ElseIf (ModifierKeys And Keys.Shift) = Keys.Shift Then
                ScrollXBy(-Math.Sign(e.Delta) * Theme.Dpi(90))
            Else
                ScrollBy(-Math.Sign(e.Delta) * Theme.Dpi(90))
            End If
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.PageUp, Keys.PageDown, Keys.Home, Keys.End : Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.Control Then
                Select Case e.KeyCode
                    Case Keys.Oemplus, Keys.Add : ZoomIn()
                    Case Keys.OemMinus, Keys.Subtract : ZoomOut()
                    Case Keys.D0, Keys.NumPad0 : SetZoomMode(PreviewZoom.FitWidth)
                    Case Else : Return
                End Select
                e.Handled = True
                e.SuppressKeyPress = True
                Return
            End If
            Select Case e.KeyCode
                Case Keys.Up : ScrollBy(-Theme.Dpi(60))
                Case Keys.Down : ScrollBy(Theme.Dpi(60))
                Case Keys.Left : ScrollXBy(-Theme.Dpi(60))
                Case Keys.Right : ScrollXBy(Theme.Dpi(60))
                Case Keys.Home : ScrollTo(0)
                Case Keys.End : ScrollTo(MaxScroll)
                Case Keys.PageUp : PageIndex -= 1
                Case Keys.PageDown : PageIndex += 1
                Case Else : Return
            End Select
            e.Handled = True
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            ClampScroll()
        End Sub

        ' ---------------------------------------------------------------- paint
        Private Sub DropBitmap()
            _bitmap?.Dispose()
            _bitmap = Nothing
            _bitmapKey = ""
            _failed = False
        End Sub

        Private Function CurrentBitmap() As Bitmap
            If _render Is Nothing OrElse _pageCount = 0 Then Return Nothing
            ' While a zoom animates, stretch the picture we already have; draw it sharp once the size settles.
            If _zoomT < 1 AndAlso _bitmap IsNot Nothing Then Return _bitmap
            Dim key = $"{_index}:{TargetW}"
            If _bitmap IsNot Nothing AndAlso key = _bitmapKey Then Return _bitmap
            If _failed AndAlso key = _bitmapKey Then Return Nothing
            _bitmap?.Dispose()
            _bitmap = Nothing
            _bitmapKey = key
            Try
                _bitmap = _render(_index, TargetW)
                _failed = False
            Catch ex As Exception
                Core.AppLog.Write(ex, "Rendering print preview page " & (_index + 1))
                _failed = True
            End Try
            Return _bitmap
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.Snow)
            Gfx.HighQuality(g)
            Dim bmp = CurrentBitmap()
            If bmp Is Nothing Then
                PaintMessage(g, If(_failed, "The preview couldn't be drawn. You can still print the report.", _message))
            Else
                PaintPage(g, bmp)
                If ShowPill Then PaintPill(g)
            End If
            If Focused Then
                Using p As New Pen(Theme.Silver, Theme.Dpi(1.0F)) With {.DashStyle = DashStyle.Dot}
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1)
                End Using
            End If
        End Sub

        Private Sub PaintPage(g As Graphics, bmp As Bitmap)
            Dim w = DisplayW
            Dim h = DisplayH
            Dim slide = CInt(Theme.Dpi(18) * (1 - _pageT) * If(_direction = 0, 1, _direction))
            Dim x = PageLeft(w) + slide
            Dim y = PageTop
            ' Soft shadow: a few widening translucent rings under the paper.
            For i = 1 To 4
                Using b As New SolidBrush(Color.FromArgb(CInt(16 * _pageT) \ i, Theme.Overlay))
                    Using path = Gfx.RoundedRect(New RectangleF(x - i + 1, y - i + 3, w + (i - 1) * 2, h + (i - 1) * 2), i * 1.5F)
                        g.FillPath(b, path)
                    End Using
                End Using
            Next
            ' Explicit pixel size: the bitmap carries a paper DPI that DrawImage(x, y) would honour. While zooming the
            ' cached picture is stretched to the animated size.
            Dim dest As New Rectangle(x, y, w, h)
            If _pageT >= 0.999 Then
                g.DrawImage(bmp, dest)
            Else
                Using attrs As New ImageAttributes()
                    attrs.SetColorMatrix(New ColorMatrix() With {.Matrix33 = CSng(Math.Max(0.05, _pageT))})
                    g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs)
                End Using
            End If
            Using p As New Pen(Theme.Mist)
                g.DrawRectangle(p, x - 1, y - 1, w + 1, h + 1)
            End Using

            ' Where else to look: quiet tags instead of scrollbars.
            If _scroll < MaxScroll Then
                DrawTag(g, "more below  ·  scroll", TagPlace.BottomRight)
            ElseIf MaxScroll > 0 AndAlso _index < _pageCount - 1 Then
                DrawTag(g, "end of page  ·  Next page for more", TagPlace.BottomRight)
            End If
            If MaxScrollX > 0 Then DrawTag(g, "drag to move the page", TagPlace.BottomLeft)
            If _busy Then DrawTag(g, "Updating preview...", TagPlace.TopLeft)
        End Sub

        Private Sub PaintPill(g As Graphics)
            Dim p = PillBounds
            Dim radius = p.Height / 2.0F
            Using path = Gfx.RoundedRect(p, radius)
                Using b As New SolidBrush(Color.FromArgb(245, Theme.White))
                    g.FillPath(b, path)
                End Using
                Dim oldClip = g.Clip
                g.SetClip(path, CombineMode.Intersect)
                If _hoverSeg >= 0 AndAlso SegmentEnabled(_hoverSeg) Then
                    Using b As New SolidBrush(If(_pressedSeg = _hoverSeg, Theme.MistPressed, Theme.SnowHover))
                        g.FillRectangle(b, SegmentBounds(_hoverSeg))
                    End Using
                End If
                g.Clip = oldClip
                oldClip.Dispose()
                Using pen As New Pen(Theme.Mist)
                    g.DrawPath(pen, path)
                    Dim inset = Theme.Dpi(7)
                    For Each i In {0, 1}
                        Dim sx = SegmentBounds(i).Right
                        g.DrawLine(pen, sx, p.Y + inset, sx, p.Bottom - inset)
                    Next
                End Using
            End Using
            ' Minus and plus as drawn lines (crisp at any DPI).
            Dim arm = Theme.Dpi(5.0F)
            For Each i In {0, 2}
                Dim r = SegmentBounds(i)
                Dim cx = r.X + r.Width / 2.0F
                Dim cy = r.Y + r.Height / 2.0F
                Using pen As New Pen(If(SegmentEnabled(i), Theme.Ink, Theme.Silver), Theme.Dpi(1.6F)) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                    g.DrawLine(pen, cx - arm, cy, cx + arm, cy)
                    If i = 2 Then g.DrawLine(pen, cx, cy - arm, cx, cy + arm)
                End Using
            Next
            TextRenderer.DrawText(g, ZoomText, Theme.LabelBold, SegmentBounds(1), Theme.Charcoal,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
        End Sub

        Private Enum TagPlace
            BottomRight
            BottomLeft
            TopLeft
        End Enum

        Private Sub DrawTag(g As Graphics, text As String, place As TagPlace)
            Dim sz = TextRenderer.MeasureText(text, Theme.Hint)
            Dim padX = Theme.Dpi(10)
            Dim w = sz.Width + padX * 2
            Dim h = sz.Height + Theme.Dpi(6)
            Dim x = If(place = TagPlace.BottomRight, Width - w - Theme.Dpi(12), Theme.Dpi(12))
            Dim y = If(place = TagPlace.TopLeft, Theme.Dpi(10), Height - h - Theme.Dpi(12))
            Dim r As New Rectangle(x, y, w, h)
            Using path = Gfx.RoundedRect(r, r.Height / 2.0F)
                Using b As New SolidBrush(Color.FromArgb(240, Theme.White))
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(Theme.Mist)
                    g.DrawPath(p, path)
                End Using
            End Using
            TextRenderer.DrawText(g, text, Theme.Hint, r, Theme.Slate, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
        End Sub

        Private Sub PaintMessage(g As Graphics, text As String)
            Dim boxW = Math.Min(Width - Theme.Dpi(48), Theme.Dpi(360))
            Dim boxH = Theme.Dpi(120)
            Dim box As New Rectangle((Width - boxW) \ 2, (Height - boxH) \ 2, boxW, boxH)
            Using p As New Pen(Theme.Silver, Theme.Dpi(1.5F))
                Gfx.DrawViewfinder(g, box, Theme.Dpi(18.0F), p)
            End Using
            TextRenderer.DrawText(g, text, Theme.Body, Rectangle.Inflate(box, -Theme.Dpi(20), -Theme.Dpi(12)), Theme.Slate,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix)
            If _busy Then DrawTag(g, "Updating preview...", TagPlace.TopLeft)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _scrollTween.Dispose()
                _pageTween.Dispose()
                _zoomTween.Dispose()
                _tip.Dispose()
                DropBitmap()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
