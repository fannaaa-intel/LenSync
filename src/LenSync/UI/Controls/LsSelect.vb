Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Controls

    ''' <summary>
    ''' Themed drop-down picker (replaces ComboBox): shows the chosen item (and an optional detail such as a price at
    ''' the right). Opens a list with no visible scrollbar; the wheel scrolls it and a quiet hint says when more items
    ''' are below. Up/Down change the choice without opening. Text = the chosen item (so Validator clears on change).
    ''' </summary>
    Public Class LsSelect
        Inherits FieldShell

        Private ReadOnly _items As New List(Of String)
        Private ReadOnly _details As New List(Of String)
        Private _selected As Integer = -1
        Private _placeholder As String = ""
        Private _icon As LineIcon?

        Public Event SelectedIndexChanged As EventHandler

        Public Property MaxVisibleItems As Integer = 8

        ''' <summary>Replaces the items. <paramref name="details"/> (optional) are drawn right-aligned in gray.</summary>
        Public Sub SetItems(items As IEnumerable(Of String), Optional details As IEnumerable(Of String) = Nothing)
            _items.Clear()
            _details.Clear()
            _items.AddRange(items)
            If details IsNot Nothing Then _details.AddRange(details)
            While _details.Count < _items.Count
                _details.Add("")
            End While
            _selected = -1
            Text = ""
            Invalidate()
        End Sub

        Public ReadOnly Property Count As Integer
            Get
                Return _items.Count
            End Get
        End Property

        Public ReadOnly Property Items As IReadOnlyList(Of String)
            Get
                Return _items
            End Get
        End Property

        Public Property Placeholder As String
            Get
                Return _placeholder
            End Get
            Set(value As String)
                _placeholder = If(value, "")
                Invalidate()
            End Set
        End Property

        Public Property Icon As LineIcon?
            Get
                Return _icon
            End Get
            Set(value As LineIcon?)
                _icon = value
                Invalidate()
            End Set
        End Property

        Public Property SelectedIndex As Integer
            Get
                Return _selected
            End Get
            Set(value As Integer)
                If value < -1 OrElse value >= _items.Count OrElse value = _selected Then Return
                _selected = value
                Text = If(value >= 0, _items(value), "")
                Invalidate()
                RaiseEvent SelectedIndexChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Protected Overrides Sub StepValue(direction As Integer)
            If _items.Count = 0 Then Return
            SelectedIndex = Math.Max(0, Math.Min(_items.Count - 1, If(_selected < 0, 0, _selected + direction)))
        End Sub

        Protected Overrides Function CreatePopupContent() As Control
            If _items.Count = 0 Then Return Nothing
            Dim list As New PopupList(_items, _details, _selected, Width, MaxVisibleItems)
            AddHandler list.Picked, Sub(s, index)
                                        SelectedIndex = index
                                        ClosePopup()
                                    End Sub
            Return list
        End Function

        Protected Overrides Sub PaintContent(g As Graphics, area As Rectangle)
            Dim x = area.Left
            If _icon.HasValue Then
                Dim s = Theme.Dpi(16.0F)
                Gfx.DrawLineIcon(g, _icon.Value, New RectangleF(x + Theme.Dpi(1), (Height - s) / 2, s, s), If(Enabled, Theme.Slate, Theme.Mist), Theme.Dpi(1.5F))
                x += Theme.Dpi(26)
            End If
            Const flags As TextFormatFlags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or
                                             TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding
            Dim textArea As New Rectangle(x, 0, Math.Max(0, area.Right - x), Height)
            If _selected < 0 Then
                TextRenderer.DrawText(g, _placeholder, Theme.Body, textArea, Theme.Silver, flags)
                Return
            End If
            Dim detail = _details(_selected)
            If detail.Length > 0 Then
                Dim dw = MeasureDetail(g, detail)
                If dw < textArea.Width \ 2 Then
                    TextRenderer.DrawText(g, detail, Theme.Label, New Rectangle(textArea.Right - dw, 0, dw, Height),
                                          If(Enabled, Theme.Slate, Theme.Silver), flags)
                    textArea.Width -= dw + Theme.Dpi(10)
                End If
            End If
            TextRenderer.DrawText(g, _items(_selected), Theme.Body, textArea, If(Enabled, Theme.Ink, Theme.Silver), flags)
        End Sub

        ''' <summary>Width of a detail text (measured without ellipsis flags, which shrink the result).</summary>
        Friend Shared Function MeasureDetail(g As Graphics, detail As String) As Integer
            Return TextRenderer.MeasureText(g, detail, Theme.Label, New Size(Integer.MaxValue, Integer.MaxValue),
                                            TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding).Width + Theme.Dpi(2)
        End Function

    End Class

    ''' <summary>The list inside LsSelect's popup.</summary>
    Friend NotInheritable Class PopupList
        Inherits Control

        Private ReadOnly _items As List(Of String)
        Private ReadOnly _details As List(Of String)
        Private ReadOnly _chosen As Integer
        Private ReadOnly _visibleRows As Integer
        Private _cursor As Integer
        Private _hover As Integer = -1
        Private _top As Integer

        Public Event Picked As EventHandler(Of Integer)

        Public Sub New(items As List(Of String), details As List(Of String), selected As Integer, width As Integer, maxRows As Integer)
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            _items = items
            _details = details
            _chosen = selected
            _cursor = Math.Max(0, selected)
            _visibleRows = Math.Max(1, Math.Min(items.Count, maxRows))
            Font = Theme.Body
            Size = New Size(width, _visibleRows * RowHeight + Pad * 2)
            ScrollIntoView(_cursor, centre:=True)
        End Sub

        Private ReadOnly Property RowHeight As Integer
            Get
                Return Theme.Dpi(36)
            End Get
        End Property

        Private ReadOnly Property Pad As Integer
            Get
                Return Theme.Dpi(4)
            End Get
        End Property

        Private Sub ScrollIntoView(index As Integer, Optional centre As Boolean = False)
            Dim maxTop = Math.Max(0, _items.Count - _visibleRows)
            If centre Then
                _top = index - _visibleRows \ 2
            ElseIf index < _top Then
                _top = index
            ElseIf index >= _top + _visibleRows Then
                _top = index - _visibleRows + 1
            End If
            _top = Math.Max(0, Math.Min(maxTop, _top))
            Invalidate()
        End Sub

        Private Function RowAt(y As Integer) As Integer
            Dim i = _top + (y - Pad) \ RowHeight
            If y < Pad OrElse i < 0 OrElse i >= _items.Count OrElse i >= _top + _visibleRows Then Return -1
            Return i
        End Function

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim h = RowAt(e.Y)
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

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            Dim i = RowAt(e.Y)
            If e.Button = MouseButtons.Left AndAlso i >= 0 Then RaiseEvent Picked(Me, i)
        End Sub

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            MyBase.OnMouseWheel(e)
            Dim maxTop = Math.Max(0, _items.Count - _visibleRows)
            _top = Math.Max(0, Math.Min(maxTop, _top - Math.Sign(e.Delta) * 2))
            _hover = RowAt(PointToClient(Cursor.Position).Y)
            Invalidate()
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData
                Case Keys.Up, Keys.Down, Keys.Enter, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown : Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            Dim target = _cursor
            Select Case e.KeyCode
                Case Keys.Up : target -= 1
                Case Keys.Down : target += 1
                Case Keys.PageUp : target -= _visibleRows
                Case Keys.PageDown : target += _visibleRows
                Case Keys.Home : target = 0
                Case Keys.End : target = _items.Count - 1
                Case Keys.Enter
                    e.Handled = True
                    RaiseEvent Picked(Me, _cursor)
                    Return
                Case Else : Return
            End Select
            e.Handled = True
            _cursor = Math.Max(0, Math.Min(_items.Count - 1, target))
            ScrollIntoView(_cursor)
        End Sub

        ''' <summary>Typing a letter jumps to the next item that starts with it.</summary>
        Protected Overrides Sub OnKeyPress(e As KeyPressEventArgs)
            MyBase.OnKeyPress(e)
            If Char.IsControl(e.KeyChar) Then Return
            For n = 1 To _items.Count
                Dim i = (_cursor + n) Mod _items.Count
                If _items(i).StartsWith(e.KeyChar.ToString(), StringComparison.OrdinalIgnoreCase) Then
                    _cursor = i
                    ScrollIntoView(i)
                    Exit For
                End If
            Next
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(Theme.White)
            Gfx.HighQuality(g)
            Const flags As TextFormatFlags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or
                                             TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding
            Dim padX = Theme.Dpi(12)
            For row = 0 To _visibleRows - 1
                Dim i = _top + row
                If i >= _items.Count Then Exit For
                Dim r As New Rectangle(Pad, Pad + row * RowHeight, Width - Pad * 2, RowHeight)
                If i = _hover OrElse i = _cursor Then
                    Using path = Gfx.RoundedRect(New RectangleF(r.X, r.Y + 1, r.Width, r.Height - 2), Theme.Dpi(4.0F))
                        Using b As New SolidBrush(If(i = _hover, Theme.SnowHover, Theme.Snow))
                            g.FillPath(b, path)
                        End Using
                    End Using
                End If
                Dim isChosen = i = _chosen
                If isChosen Then
                    Using b As New SolidBrush(Theme.Ink)
                        g.FillRectangle(b, r.X, r.Y + Theme.Dpi(9), Theme.Dpi(3), r.Height - Theme.Dpi(18))
                    End Using
                End If
                Dim textArea As New Rectangle(r.X + padX, r.Y, r.Width - padX * 2, r.Height)
                Dim detail = _details(i)
                If detail.Length > 0 Then
                    Dim dw = LsSelect.MeasureDetail(g, detail)
                    TextRenderer.DrawText(g, detail, Theme.Label, New Rectangle(textArea.Right - dw, r.Y, dw, r.Height), Theme.Slate, flags)
                    textArea.Width -= dw + Theme.Dpi(12)
                End If
                TextRenderer.DrawText(g, _items(i), If(isChosen, Theme.BodyBold, Theme.Body), textArea, Theme.Ink, flags)
            Next
            ' More items below/above: a quiet hint instead of a scrollbar.
            If _top + _visibleRows < _items.Count Then
                Dim hint = $"{_items.Count - _top - _visibleRows} more  ·  scroll"
                Dim sz = TextRenderer.MeasureText(hint, Theme.Hint)
                Dim hr As New Rectangle(Width - sz.Width - Theme.Dpi(14), Height - sz.Height - Theme.Dpi(6), sz.Width + Theme.Dpi(8), sz.Height + Theme.Dpi(2))
                Using b As New SolidBrush(Color.FromArgb(235, Theme.White))
                    g.FillRectangle(b, hr)
                End Using
                TextRenderer.DrawText(g, hint, Theme.Hint, hr, Theme.Slate, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End If
            Using p As New Pen(Theme.Silver)
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1)
            End Using
        End Sub

    End Class

End Namespace
