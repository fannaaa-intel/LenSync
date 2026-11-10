Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Controls

    ''' <summary>
    ''' Segmented picker for a few fixed options (e.g. roles). The chosen segment is a solid ink pill that glides to
    ''' the new choice. Keyboard: Tab to it, Left/Right to change. Text = the chosen option (so Validator clears on change).
    ''' </summary>
    Public Class LsChoice
        Inherits Control

        Private ReadOnly _items As New List(Of String)
        Private _selected As Integer = -1
        Private _thumbX As Single
        Private _thumbFrom As Single
        Private _hover As Integer = -1
        Private ReadOnly _tween As New Tween()

        Public Event SelectedIndexChanged As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable Or ControlStyles.SupportsTransparentBackColor, True)
            TabStop = True
            Font = Theme.Body
            Cursor = Cursors.Hand
            Height = Theme.Dpi(42)
            AccessibleRole = AccessibleRole.PageTabList
        End Sub

        Public Sub SetItems(items As IEnumerable(Of String))
            _items.Clear()
            _items.AddRange(items)
            _selected = -1
            Invalidate()
        End Sub

        Public Property SelectedIndex As Integer
            Get
                Return _selected
            End Get
            Set(value As Integer)
                If value < -1 OrElse value >= _items.Count OrElse value = _selected Then Return
                Dim first = _selected < 0
                _selected = value
                Text = If(value >= 0, _items(value), "")
                AccessibleName = Text
                If first OrElse Not IsHandleCreated Then
                    _thumbX = SegmentLeft(value)
                    Invalidate()
                Else
                    _thumbFrom = _thumbX
                    Dim target = SegmentLeft(value)
                    _tween.Start(Theme.OpenMs, Sub(t)
                                                   _thumbX = _thumbFrom + (target - _thumbFrom) * CSng(Easing.OutCubic(t))
                                                   Invalidate()
                                               End Sub)
                End If
                RaiseEvent SelectedIndexChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Private ReadOnly Property SegmentWidth As Single
            Get
                Return If(_items.Count = 0, Width, (Width - Theme.Dpi(8.0F)) / _items.Count)
            End Get
        End Property

        Private Function SegmentLeft(index As Integer) As Single
            Return Theme.Dpi(4.0F) + Math.Max(0, index) * SegmentWidth
        End Function

        Private Function HitTest(x As Integer) As Integer
            If _items.Count = 0 Then Return -1
            Dim i = CInt(Math.Floor((x - Theme.Dpi(4.0F)) / SegmentWidth))
            Return Math.Max(0, Math.Min(_items.Count - 1, i))
        End Function

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            If _selected >= 0 AndAlso Not _tween.IsRunning Then _thumbX = SegmentLeft(_selected)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim h = HitTest(e.X)
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
            If Not Enabled Then Return
            Focus()
            SelectedIndex = HitTest(e.X)
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Return keyData = Keys.Left OrElse keyData = Keys.Right OrElse MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If _items.Count = 0 Then Return
            If e.KeyCode = Keys.Left Then SelectedIndex = Math.Max(0, _selected - 1)
            If e.KeyCode = Keys.Right Then SelectedIndex = Math.Min(_items.Count - 1, _selected + 1)
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnEnabledChanged(e As EventArgs)
            MyBase.OnEnabledChanged(e)
            Cursor = If(Enabled, Cursors.Hand, Cursors.Default)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim track As New RectangleF(0.5F, 0.5F, Width - 1.0F, Height - 1.0F)
            Using path = Gfx.RoundedRect(track, Theme.Dpi(CSng(Theme.RadiusSmall)))
                Using b As New SolidBrush(Theme.Snow)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(If(Focused, Theme.Ink, Theme.Mist), If(Focused, Theme.Dpi(1.5F), 1.0F))
                    g.DrawPath(p, path)
                End Using
            End Using
            If _items.Count = 0 Then Return
            Dim segW = SegmentWidth
            Dim inner = Theme.Dpi(4.0F)
            If _selected >= 0 Then
                Dim thumb As New RectangleF(_thumbX, inner, segW, Height - inner * 2)
                Using path = Gfx.RoundedRect(thumb, Theme.Dpi(4.0F))
                    Using b As New SolidBrush(If(Enabled, Theme.Ink, Theme.Silver))
                        g.FillPath(b, path)
                    End Using
                End Using
            End If
            For i = 0 To _items.Count - 1
                Dim r As New Rectangle(CInt(SegmentLeft(i)), 0, CInt(segW), Height)
                Dim isSel = i = _selected
                Dim fore = If(isSel, Theme.White, If(i = _hover AndAlso Enabled, Theme.Ink, Theme.Slate))
                If Not Enabled AndAlso Not isSel Then fore = Theme.Silver
                TextRenderer.DrawText(g, _items(i), If(isSel, Theme.BodyBold, Theme.Body), r, fore,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
            Next
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
