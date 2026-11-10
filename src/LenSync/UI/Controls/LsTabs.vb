Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Controls

    ''' <summary>
    ''' Text tabs with a thick ink underline that glides to the selected tab. The selected tab is semibold ink,
    ''' others slate. Keyboard: Tab to it, Left/Right to switch.
    ''' </summary>
    Public Class LsTabs
        Inherits Control

        Private ReadOnly _items As New List(Of String)
        Private ReadOnly _bounds As New List(Of Rectangle)
        Private _selected As Integer = -1
        Private _hover As Integer = -1
        Private _lineX As Single
        Private _lineW As Single
        Private ReadOnly _tween As New Tween()

        Public Event SelectedIndexChanged As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            TabStop = True
            BackColor = Theme.Snow
            Height = Theme.Dpi(48)
            AccessibleRole = AccessibleRole.PageTabList
        End Sub

        Public Sub SetItems(items As IEnumerable(Of String))
            _items.Clear()
            _items.AddRange(items)
            Measure()
            Invalidate()
        End Sub

        Public Property SelectedIndex As Integer
            Get
                Return _selected
            End Get
            Set(value As Integer)
                If value < 0 OrElse value >= _items.Count OrElse value = _selected Then Return
                Dim first = _selected < 0
                _selected = value
                AccessibleName = _items(value)
                Measure()
                Dim target = _bounds(value)
                If first OrElse Not IsHandleCreated Then
                    _lineX = target.X
                    _lineW = target.Width
                    Invalidate()
                Else
                    Dim fromX = _lineX
                    Dim fromW = _lineW
                    _tween.Start(Theme.OpenMs, Sub(t)
                                                   Dim k = CSng(Easing.OutCubic(t))
                                                   _lineX = fromX + (target.X - fromX) * k
                                                   _lineW = fromW + (target.Width - fromW) * k
                                                   Invalidate()
                                               End Sub)
                End If
                RaiseEvent SelectedIndexChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Private Sub Measure()
            _bounds.Clear()
            Dim x = 0
            Dim gap = Theme.Dpi(30)
            For Each item In _items
                Dim w = TextRenderer.MeasureText(item, Theme.BodyBold).Width
                _bounds.Add(New Rectangle(x, 0, w, Height))
                x += w + gap
            Next
        End Sub

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            Measure()
            If _selected >= 0 AndAlso Not _tween.IsRunning Then
                _lineX = _bounds(_selected).X
                _lineW = _bounds(_selected).Width
            End If
        End Sub

        Private Function HitTest(p As Point) As Integer
            For i = 0 To _bounds.Count - 1
                Dim r = _bounds(i)
                r.Inflate(Theme.Dpi(12), 0)
                If r.Contains(p) Then Return i
            Next
            Return -1
        End Function

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim h = HitTest(e.Location)
            Cursor = If(h >= 0, Cursors.Hand, Cursors.Default)
            If h <> _hover Then
                _hover = h
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = -1
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Dim h = HitTest(e.Location)
            If h >= 0 Then SelectedIndex = h
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Return keyData = Keys.Left OrElse keyData = Keys.Right OrElse MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Left AndAlso _selected > 0 Then SelectedIndex = _selected - 1
            If e.KeyCode = Keys.Right AndAlso _selected < _items.Count - 1 Then SelectedIndex = _selected + 1
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            Gfx.HighQuality(g)
            Using p As New Pen(Theme.Mist)
                g.DrawLine(p, 0, Height - 1, Width, Height - 1)
            End Using
            For i = 0 To _items.Count - 1
                Dim isSel = i = _selected
                Dim fore = If(isSel, Theme.Ink, If(i = _hover, Theme.Charcoal, Theme.Slate))
                TextRenderer.DrawText(g, _items(i), If(isSel, Theme.BodyBold, Theme.Body), _bounds(i), fore,
                                      TextFormatFlags.VerticalCenter Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
            Next
            If _selected >= 0 Then
                Using b As New SolidBrush(Theme.Ink)
                    g.FillRectangle(b, _lineX, Height - Theme.Dpi(3.0F), _lineW, Theme.Dpi(3.0F))
                End Using
                If Focused AndAlso ShowFocusCues Then
                    Dim r = _bounds(_selected)
                    r.Inflate(Theme.Dpi(8), -Theme.Dpi(10))
                    Using p As New Pen(Theme.Slate) With {.DashStyle = Drawing2D.DashStyle.Dot}
                        g.DrawRectangle(p, r)
                    End Using
                End If
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
