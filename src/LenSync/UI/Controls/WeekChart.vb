Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports LenSync.Core
Imports LenSync.Models

Namespace UI.Controls

    ''' <summary>
    ''' Dashboard "Sales, last 7 days": one column per day, the amount printed on top of each bar (no y-axis), today in
    ''' ink inside viewfinder brackets, days without sales as a flat stub. Hover or Left/Right shows a small card with
    ''' the date, amount and receipts; click / Enter raises DayClicked. Bars grow in, and glide to new heights when the
    ''' dashboard refreshes.
    ''' </summary>
    Public Class WeekChart
        Inherits Control

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private Const GrowMs As Integer = 240

        Private _days As New List(Of DaySales)
        Private _today As Date = Date.Today
        Private _from As Decimal() = {}
        Private _to As Decimal() = {}
        Private _t As Double = 1
        Private _hover As Integer = -1
        Private _keyIndex As Integer = -1
        Private ReadOnly _tween As New Tween()

        Public Event DayClicked As EventHandler(Of Date)

        ''' <summary>True when clicking a day does something (the hand cursor shows).</summary>
        Public Property Clickable As Boolean

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            TabStop = True
            BackColor = Theme.White
            AccessibleRole = AccessibleRole.Chart
        End Sub

        ''' <summary>Shows <paramref name="days"/> (oldest first). Bars animate from their current heights.</summary>
        Public Sub SetDays(days As IList(Of DaySales), today As Date)
            Dim current = CurrentValues()
            _days = days.ToList()
            _today = today.Date
            _to = _days.Select(Function(d) d.Total).ToArray()
            _from = If(current.Length = _to.Length, current, New Decimal(_to.Length - 1) {})
            If _hover >= _days.Count Then _hover = -1
            If _keyIndex >= _days.Count Then _keyIndex = -1
            AccessibleDescription = String.Join("; ", _days.Select(Function(d) $"{DayName(d.Day, True)} {Money.Format(d.Total)}"))
            If _from.SequenceEqual(_to) OrElse Not IsHandleCreated Then
                _t = 1
                Invalidate()
                Return
            End If
            _tween.Start(GrowMs, Sub(t)
                                     _t = t
                                     Invalidate()
                                 End Sub)
        End Sub

        Private Function CurrentValues() As Decimal()
            If _to.Length = 0 Then Return _to
            Dim k = CDec(Easing.OutCubic(_t))
            Return _to.Select(Function(v, i) If(i < _from.Length, _from(i) + (v - _from(i)) * k, v)).ToArray()
        End Function

        Private Function DayName(d As Date, full As Boolean) As String
            If d = _today Then Return If(full, "Today, " & d.ToString("MMM d", PH), "Today")
            Return d.ToString(If(full, "ddd, MMM d", "ddd d"), PH)
        End Function

        ' ---------------------------------------------------------------- geometry
        Private ReadOnly Property AxisH As Integer
            Get
                Return Theme.Label.Height + Theme.Dpi(10)
            End Get
        End Property

        Private ReadOnly Property ValueH As Integer
            Get
                Return Theme.LabelBold.Height + Theme.Dpi(4)
            End Get
        End Property

        Private ReadOnly Property Baseline As Integer
            Get
                Return Height - AxisH
            End Get
        End Property

        Private ReadOnly Property PlotTop As Integer
            Get
                Return Theme.Dpi(6) + ValueH
            End Get
        End Property

        Private Function Slot(i As Integer) As Rectangle
            Dim n = Math.Max(1, _days.Count)
            Dim x0 = CInt(Width * i / n)
            Dim x1 = CInt(Width * (i + 1) / n)
            Return New Rectangle(x0, 0, x1 - x0, Height)
        End Function

        Private Function BarRect(i As Integer, values As Decimal(), max As Decimal) As Rectangle
            Dim s = Slot(i)
            Dim w = Math.Min(Theme.Dpi(40), Math.Max(Theme.Dpi(10), CInt(s.Width * 0.46)))
            Dim plotH = Math.Max(1, Baseline - PlotTop)
            Dim v = values(i)
            Dim h = If(v <= 0D OrElse max <= 0D, 0, Math.Max(Theme.Dpi(4), CInt(plotH * CDbl(v / max))))
            Return New Rectangle(s.X + (s.Width - w) \ 2, Baseline - h, w, h)
        End Function

        Private Function IndexAt(p As Point) As Integer
            If _days.Count = 0 OrElse p.Y < 0 OrElse p.Y > Height Then Return -1
            For i = 0 To _days.Count - 1
                If Slot(i).Contains(p) Then Return i
            Next
            Return -1
        End Function

        Private ReadOnly Property ActiveIndex As Integer
            Get
                Return If(_hover >= 0, _hover, If(Focused, _keyIndex, -1))
            End Get
        End Property

        ' ---------------------------------------------------------------- input
        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim i = IndexAt(e.Location)
            If i <> _hover Then
                _hover = i
                Cursor = If(i >= 0 AndAlso Clickable, Cursors.Hand, Cursors.Default)
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = -1
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            Dim i = IndexAt(e.Location)
            If e.Button = MouseButtons.Left AndAlso Clickable AndAlso i >= 0 Then RaiseEvent DayClicked(Me, _days(i).Day)
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Return keyData = Keys.Left OrElse keyData = Keys.Right OrElse MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If _days.Count = 0 Then Return
            Select Case e.KeyCode
                Case Keys.Left : _keyIndex = Math.Max(0, If(_keyIndex < 0, _days.Count - 1, _keyIndex - 1))
                Case Keys.Right : _keyIndex = Math.Min(_days.Count - 1, If(_keyIndex < 0, _days.Count - 1, _keyIndex + 1))
                Case Keys.Enter
                    If Clickable AndAlso _keyIndex >= 0 Then RaiseEvent DayClicked(Me, _days(_keyIndex).Day)
                Case Else : Return
            End Select
            e.Handled = True
            Invalidate()
        End Sub

        ' The chart can receive focus just because the Dashboard opened, so nothing is highlighted until an arrow key
        ' is actually pressed (otherwise a detail card would sit over the bars all the time).
        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            _keyIndex = -1
            Invalidate()
        End Sub

        ' ---------------------------------------------------------------- paint
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.White)
            Gfx.HighQuality(g)
            If _days.Count = 0 Then Return
            Dim values = CurrentValues()
            Dim max = Math.Max(_to.DefaultIfEmpty(0D).Max(), values.DefaultIfEmpty(0D).Max())
            Dim active = ActiveIndex
            Const center As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine

            ' The highlighted day: a soft rounded backdrop behind its column.
            If active >= 0 Then
                Dim s = Slot(active)
                Using path = Gfx.RoundedRect(New RectangleF(s.X + Theme.Dpi(4), Theme.Dpi(2), s.Width - Theme.Dpi(8), Height - Theme.Dpi(2)), Theme.Dpi(6.0F))
                    Using b As New SolidBrush(Theme.Snow)
                        g.FillPath(b, path)
                    End Using
                End Using
            End If

            ' Baseline.
            Using p As New Pen(Theme.Mist)
                g.DrawLine(p, 0, Baseline, Width, Baseline)
            End Using

            For i = 0 To _days.Count - 1
                Dim d = _days(i)
                Dim isToday = d.Day = _today
                Dim s = Slot(i)
                Dim bar = BarRect(i, values, max)
                If bar.Height > 0 Then
                    Dim color = If(isToday, Theme.Ink, If(i = active, Theme.Slate, Theme.Silver))
                    Using path = TopRounded(bar, Theme.Dpi(4.0F))
                        Using b As New SolidBrush(color)
                            g.FillPath(b, path)
                        End Using
                    End Using
                Else
                    ' No sales: a flat stub on the baseline, so the day still reads as "₱0".
                    Dim stub As New RectangleF(bar.X, Baseline - Theme.Dpi(3), bar.Width, Theme.Dpi(3))
                    Using path = Gfx.RoundedRect(stub, Theme.Dpi(1.5F))
                        Using b As New SolidBrush(If(isToday, Theme.Slate, Theme.Mist))
                            g.FillPath(b, path)
                        End Using
                    End Using
                End If

                ' Amount on top of the bar (always for today, otherwise only when there were sales).
                If _to(i) > 0D OrElse isToday Then
                    Dim top = If(bar.Height > 0, bar.Top, Baseline - Theme.Dpi(3)) - ValueH
                    Dim txt = "₱" & Decimal.Round(values(i), 0).ToString("#,##0", CultureInfo.InvariantCulture)
                    TextRenderer.DrawText(g, txt, If(isToday, Theme.LabelBold, Theme.Hint), New Rectangle(s.X - Theme.Dpi(8), top, s.Width + Theme.Dpi(16), ValueH),
                                          If(isToday OrElse i = active, Theme.Ink, Theme.Slate), center)
                End If

                ' Day label under the baseline.
                TextRenderer.DrawText(g, DayName(d.Day, False), If(isToday, Theme.LabelBold, Theme.Label),
                                      New Rectangle(s.X, Baseline + Theme.Dpi(2), s.Width, AxisH - Theme.Dpi(2)), If(isToday, Theme.Ink, Theme.Slate), center)

                ' Today sits inside viewfinder brackets (LENSYNC's motif).
                If isToday Then
                    Dim fr As New RectangleF(s.X + Theme.Dpi(6), Theme.Dpi(3), s.Width - Theme.Dpi(12), Height - Theme.Dpi(4))
                    Using p As New Pen(Theme.Silver, Theme.Dpi(1.2F))
                        Gfx.DrawViewfinder(g, fr, Theme.Dpi(7.0F), p)
                    End Using
                End If
            Next

            If _to.All(Function(v) v = 0D) Then
                TextRenderer.DrawText(g, "No completed sales in the last 7 days", Theme.Label,
                                      New Rectangle(0, PlotTop, Width, Math.Max(0, Baseline - PlotTop - Theme.Dpi(8))), Theme.Slate, center)
            End If

            If active >= 0 Then DrawDetail(g, active, BarRect(active, values, max))
        End Sub

        ''' <summary>A small card next to the highlighted bar: date, amount, receipts.</summary>
        Private Sub DrawDetail(g As Graphics, i As Integer, bar As Rectangle)
            Dim d = _days(i)
            Dim line1 = DayName(d.Day, True)
            Dim line2 = Money.Format(d.Total)
            Dim line3 = If(d.Receipts = 0, "No receipts", $"{d.Receipts} receipt{If(d.Receipts = 1, "", "s")}") & If(Clickable, "  ·  click for Sales", "")
            Const flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding
            Dim w = {TextRenderer.MeasureText(g, line1, Theme.Hint, Size.Empty, flags).Width,
                     TextRenderer.MeasureText(g, line2, Theme.BodyBold, Size.Empty, flags).Width,
                     TextRenderer.MeasureText(g, line3, Theme.Hint, Size.Empty, flags).Width}.Max() + Theme.Dpi(24)
            Dim h = Theme.Hint.Height * 2 + Theme.BodyBold.Height + Theme.Dpi(16)
            Dim s = Slot(i)
            ' Beside the column (right of it, or left near the right edge), level with the top of the bar.
            Dim x = s.Right - Theme.Dpi(4)
            If x + w > Width Then x = s.Left - w + Theme.Dpi(4)
            x = Math.Max(0, Math.Min(Width - w, x))
            Dim y = Math.Max(Theme.Dpi(2), Math.Min(Baseline - h - Theme.Dpi(4), If(bar.Height > 0, bar.Top, Baseline) - h \ 2))
            Dim card As New Rectangle(x, y, w, h)
            For k = 1 To 3
                Using b As New SolidBrush(Color.FromArgb(14 \ k, Theme.Overlay))
                    Using path = Gfx.RoundedRect(New RectangleF(card.X - k + 1, card.Y - k + 3, card.Width + (k - 1) * 2, card.Height + (k - 1) * 2), Theme.Dpi(6.0F) + k)
                        g.FillPath(b, path)
                    End Using
                End Using
            Next
            Using path = Gfx.RoundedRect(card, Theme.Dpi(6.0F))
                Using b As New SolidBrush(Theme.White)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(Theme.Mist)
                    g.DrawPath(p, path)
                End Using
            End Using
            Dim tx = card.X + Theme.Dpi(12)
            Dim ty = card.Y + Theme.Dpi(8)
            TextRenderer.DrawText(g, line1, Theme.Hint, New Point(tx, ty), Theme.Slate, flags)
            ty += Theme.Hint.Height
            TextRenderer.DrawText(g, line2, Theme.BodyBold, New Point(tx, ty), Theme.Ink, flags)
            ty += Theme.BodyBold.Height
            TextRenderer.DrawText(g, line3, Theme.Hint, New Point(tx, ty), Theme.Slate, flags)
        End Sub

        ''' <summary>A rectangle with only its top corners rounded (a bar standing on the baseline).</summary>
        Private Shared Function TopRounded(r As Rectangle, radius As Single) As GraphicsPath
            Dim path As New GraphicsPath()
            Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height * 2))
            If d < 1 Then
                path.AddRectangle(r)
                Return path
            End If
            path.AddArc(r.X, r.Y, d, d, 180, 90)
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90)
            path.AddLine(r.Right, r.Y + d / 2, r.Right, r.Bottom)
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom)
            path.CloseFigure()
            Return path
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
