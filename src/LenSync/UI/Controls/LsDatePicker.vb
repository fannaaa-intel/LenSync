Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization

Namespace UI.Controls

    ''' <summary>
    ''' Themed date field: shows "Thu, Nov 12, 2026" with a calendar icon and opens a monochrome month calendar.
    ''' Up/Down move one day without opening. Text = the formatted date (so Validator clears on change).
    ''' </summary>
    Public Class LsDatePicker
        Inherits FieldShell

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private _value As Date = Date.Today
        Private _minDate As Date = Date.MinValue

        Public Event ValueChanged As EventHandler

        Public Sub New()
            Text = Format(_value)
        End Sub

        Public Shared Function Format(d As Date) As String
            Return d.ToString("ddd, MMM d, yyyy", PH)
        End Function

        Public Property Value As Date
            Get
                Return _value
            End Get
            Set(v As Date)
                v = v.Date
                If v = _value Then Return
                _value = v
                Text = Format(v)
                Invalidate()
                RaiseEvent ValueChanged(Me, EventArgs.Empty)
            End Set
        End Property

        ''' <summary>Days before this are greyed out in the calendar (they can still be shown, e.g. an old booking).</summary>
        Public Property MinDate As Date
            Get
                Return _minDate
            End Get
            Set(v As Date)
                _minDate = v.Date
            End Set
        End Property

        Protected Overrides Sub StepValue(direction As Integer)
            Dim target = _value.AddDays(direction)
            If target >= _minDate Then Value = target
        End Sub

        Protected Overrides Function CreatePopupContent() As Control
            Dim cal As New CalendarView(_value, _minDate)
            AddHandler cal.Picked, Sub(s, d)
                                       Value = d
                                       ClosePopup()
                                   End Sub
            Return cal
        End Function

        Protected Overrides Sub PaintContent(g As Graphics, area As Rectangle)
            Dim s = Theme.Dpi(16.0F)
            Gfx.DrawLineIcon(g, LineIcon.Bookings, New RectangleF(area.Left + Theme.Dpi(1), (Height - s) / 2, s, s),
                             If(Enabled, Theme.Slate, Theme.Mist), Theme.Dpi(1.5F))
            Dim x = area.Left + Theme.Dpi(26)
            TextRenderer.DrawText(g, Text, Theme.Body, New Rectangle(x, 0, Math.Max(0, area.Right - x), Height), If(Enabled, Theme.Ink, Theme.Silver),
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
        End Sub

    End Class

    ''' <summary>
    ''' Month calendar for LsDatePicker's popup. Selected day = solid ink circle, today = outline ring, days before the
    ''' minimum are grey. Keyboard: arrows move a day/week, PageUp/PageDown change month, Enter picks.
    ''' </summary>
    Friend NotInheritable Class CalendarView
        Inherits Control

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private Shared ReadOnly DayNames As String() = {"Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"}
        Private ReadOnly _selected As Date
        Private ReadOnly _min As Date
        Private _month As Date
        Private _cursor As Date
        Private _hoverDay As Date?
        Private _hoverArrow As Integer   ' -1 prev, 1 next, 0 none

        Public Event Picked As EventHandler(Of Date)

        Public Sub New(selected As Date, minDate As Date)
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            _selected = selected.Date
            _min = minDate.Date
            _cursor = _selected
            _month = New Date(_selected.Year, _selected.Month, 1)
            Size = New Size(Pad * 2 + Cell * 7, Pad + HeaderH + WeekH + Cell * 6 + Pad)
        End Sub

        Private ReadOnly Property Cell As Integer
            Get
                Return Theme.Dpi(36)
            End Get
        End Property
        Private ReadOnly Property Pad As Integer
            Get
                Return Theme.Dpi(12)
            End Get
        End Property
        Private ReadOnly Property HeaderH As Integer
            Get
                Return Theme.Dpi(40)
            End Get
        End Property
        Private ReadOnly Property WeekH As Integer
            Get
                Return Theme.Dpi(26)
            End Get
        End Property

        Private ReadOnly Property GridStart As Date
            Get
                Return _month.AddDays(-CInt(_month.DayOfWeek))
            End Get
        End Property

        Private Function ArrowRect(direction As Integer) As Rectangle
            Dim s = Theme.Dpi(32)
            Dim right = Width - Pad
            Dim x = If(direction > 0, right - s, right - s * 2 - Theme.Dpi(4))
            Return New Rectangle(x, Pad + (HeaderH - s) \ 2, s, s)
        End Function

        Private Function DayRect(index As Integer) As Rectangle
            Return New Rectangle(Pad + (index Mod 7) * Cell, Pad + HeaderH + WeekH + (index \ 7) * Cell, Cell, Cell)
        End Function

        Private Function DayAt(p As Point) As Date?
            For i = 0 To 41
                If DayRect(i).Contains(p) Then Return GridStart.AddDays(i)
            Next
            Return Nothing
        End Function

        Private Sub ShowMonth(d As Date)
            _month = New Date(d.Year, d.Month, 1)
            Invalidate()
        End Sub

        Private Sub MoveCursor(d As Date)
            _cursor = d
            If d < _month OrElse d >= _month.AddMonths(1) Then ShowMonth(d)
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim arrow = If(ArrowRect(-1).Contains(e.Location), -1, If(ArrowRect(1).Contains(e.Location), 1, 0))
            Dim day = DayAt(e.Location)
            If arrow <> _hoverArrow OrElse Not Nullable.Equals(day, _hoverDay) Then
                _hoverArrow = arrow
                _hoverDay = day
                Cursor = If(arrow <> 0 OrElse (day.HasValue AndAlso day.Value >= _min), Cursors.Hand, Cursors.Default)
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hoverArrow = 0
            _hoverDay = Nothing
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If e.Button <> MouseButtons.Left Then Return
            If ArrowRect(-1).Contains(e.Location) Then
                ShowMonth(_month.AddMonths(-1))
            ElseIf ArrowRect(1).Contains(e.Location) Then
                ShowMonth(_month.AddMonths(1))
            Else
                Dim day = DayAt(e.Location)
                If day.HasValue AndAlso day.Value >= _min Then RaiseEvent Picked(Me, day.Value)
            End If
        End Sub

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            MyBase.OnMouseWheel(e)
            ShowMonth(_month.AddMonths(-Math.Sign(e.Delta)))
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.PageUp, Keys.PageDown, Keys.Home : Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            Select Case e.KeyCode
                Case Keys.Left : MoveCursor(_cursor.AddDays(-1))
                Case Keys.Right : MoveCursor(_cursor.AddDays(1))
                Case Keys.Up : MoveCursor(_cursor.AddDays(-7))
                Case Keys.Down : MoveCursor(_cursor.AddDays(7))
                Case Keys.PageUp : MoveCursor(_cursor.AddMonths(-1))
                Case Keys.PageDown : MoveCursor(_cursor.AddMonths(1))
                Case Keys.Home : MoveCursor(Date.Today)
                Case Keys.Enter
                    If _cursor >= _min Then RaiseEvent Picked(Me, _cursor)
                Case Else : Return
            End Select
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.White)
            Gfx.HighQuality(g)
            Const center As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine

            ' Header: month and year, previous/next.
            TextRenderer.DrawText(g, _month.ToString("MMMM yyyy", PH), Theme.BodyBold,
                                  New Rectangle(Pad + Theme.Dpi(6), Pad, Width - Pad * 2, HeaderH), Theme.Ink,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
            For Each arrow In {-1, 1}
                Dim r = ArrowRect(arrow)
                If _hoverArrow = arrow Then
                    Using path = Gfx.RoundedRect(r, Theme.Dpi(4.0F))
                        Using b As New SolidBrush(Theme.SnowHover)
                            g.FillPath(b, path)
                        End Using
                    End Using
                End If
                Gfx.DrawChevron(g, r, Theme.Ink, Theme.Dpi(1.5F), horizontal:=arrow)
            Next

            ' Weekday names.
            For i = 0 To 6
                TextRenderer.DrawText(g, DayNames(i), Theme.Caption, New Rectangle(Pad + i * Cell, Pad + HeaderH, Cell, WeekH), Theme.Slate, center)
            Next

            ' Days.
            Dim start = GridStart
            Dim inset = Theme.Dpi(3)
            For i = 0 To 41
                Dim d = start.AddDays(i)
                Dim r = DayRect(i)
                Dim circle As New RectangleF(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2)
                Dim inMonth = d.Month = _month.Month
                Dim enabled = d >= _min
                Dim isSel = d = _selected
                Dim fore = If(Not enabled, Theme.Mist, If(inMonth, Theme.Ink, Theme.Silver))
                If isSel Then
                    Using b As New SolidBrush(Theme.Ink)
                        g.FillEllipse(b, circle)
                    End Using
                    fore = Theme.White
                ElseIf enabled AndAlso _hoverDay.HasValue AndAlso _hoverDay.Value = d Then
                    Using b As New SolidBrush(Theme.SnowHover)
                        g.FillEllipse(b, circle)
                    End Using
                End If
                If d = Date.Today AndAlso Not isSel Then
                    Using p As New Pen(Theme.Slate, Theme.Dpi(1.2F))
                        g.DrawEllipse(p, circle)
                    End Using
                End If
                If d = _cursor AndAlso Focused AndAlso Not isSel Then
                    Using p As New Pen(Theme.Ink, Theme.Dpi(1.2F)) With {.DashStyle = Drawing2D.DashStyle.Dot}
                        g.DrawEllipse(p, circle)
                    End Using
                End If
                TextRenderer.DrawText(g, d.Day.ToString(CultureInfo.InvariantCulture), If(isSel OrElse d = Date.Today, Theme.BodyBold, Theme.Body), r, fore, center)
            Next

            Using p As New Pen(Theme.Silver)
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1)
            End Using
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

    End Class

End Namespace
