Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Controls

    ''' <summary>
    ''' Monochrome read-only list. No visible scrollbars (wheel and arrow keys still scroll), an ink bar marks the
    ''' selected row, "status" columns draw as pills, a hint appears when more rows are below, and an empty-state
    ''' message shows when there are no rows. Columns: add with AddTextColumn / AddStatusColumn.
    ''' </summary>
    Public Class LsGrid
        Inherits DataGridView

        Private ReadOnly _statusColumns As New HashSet(Of String)
        Private _emptyText As String = "Nothing here yet."

        ''' <summary>
        ''' Status texts drawn as a solid ink pill (the state that matters most on that list, e.g. "Active" or
        ''' "Unpaid"); every other status is an outline. Weight, not color, carries the meaning.
        ''' </summary>
        Public ReadOnly Property SolidStatuses As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"Active"}

        Public Sub New()
            DoubleBuffered = True
            BorderStyle = BorderStyle.None
            BackgroundColor = Theme.White
            GridColor = Theme.Mist
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
            EnableHeadersVisualStyles = False
            RowHeadersVisible = False
            AllowUserToAddRows = False
            AllowUserToDeleteRows = False
            AllowUserToResizeRows = False
            AllowUserToResizeColumns = False
            AllowUserToOrderColumns = False
            Me.ReadOnly = True
            MultiSelect = False
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            ScrollBars = ScrollBars.None
            StandardTab = True
            ShowCellToolTips = True

            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            ColumnHeadersHeight = Theme.Dpi(40)
            With ColumnHeadersDefaultCellStyle
                .BackColor = Theme.White
                .ForeColor = Theme.Slate
                .SelectionBackColor = Theme.White
                .SelectionForeColor = Theme.Slate
                .Font = Theme.LabelBold
                .Padding = New Padding(Theme.Dpi(14), 0, Theme.Dpi(8), 0)
                .WrapMode = DataGridViewTriState.False
            End With
            RowTemplate.Height = Theme.Dpi(46)
            With DefaultCellStyle
                .BackColor = Theme.White
                .ForeColor = Theme.Charcoal
                .SelectionBackColor = Theme.Snow
                .SelectionForeColor = Theme.Ink
                .Font = Theme.Body
                .Padding = New Padding(Theme.Dpi(14), 0, Theme.Dpi(8), 0)
                .WrapMode = DataGridViewTriState.False
            End With
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

        Public Function AddTextColumn(name As String, header As String, fillWeight As Single, Optional alignRight As Boolean = False) As DataGridViewTextBoxColumn
            Dim c As New DataGridViewTextBoxColumn With {.Name = name, .HeaderText = header, .FillWeight = fillWeight,
                                                         .SortMode = DataGridViewColumnSortMode.NotSortable}
            If alignRight Then
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
                c.DefaultCellStyle.Padding = New Padding(Theme.Dpi(8), 0, Theme.Dpi(18), 0)
                c.HeaderCell.Style.Padding = New Padding(Theme.Dpi(8), 0, Theme.Dpi(18), 0)
            End If
            Columns.Add(c)
            Return c
        End Function

        ''' <summary>A column whose text ("Active", "Inactive", "Locked"...) is drawn as a pill.</summary>
        Public Function AddStatusColumn(name As String, header As String, fillWeight As Single) As DataGridViewTextBoxColumn
            Dim c = AddTextColumn(name, header, fillWeight)
            _statusColumns.Add(name)
            Return c
        End Function

        ''' <summary>The Tag of the selected row (callers store their item object there), or Nothing.</summary>
        Public ReadOnly Property SelectedItem As Object
            Get
                Return If(CurrentRow Is Nothing, Nothing, CurrentRow.Tag)
            End Get
        End Property

        ''' <summary>Re-selects the row whose Tag matches <paramref name="match"/> after a reload.</summary>
        Public Sub SelectWhere(match As Func(Of Object, Boolean))
            For Each row As DataGridViewRow In Rows
                If match(row.Tag) Then
                    CurrentCell = row.Cells(FirstVisibleColumnIndex())
                    EnsureVisible(row.Index)
                    Return
                End If
            Next
        End Sub

        Private Function FirstVisibleColumnIndex() As Integer
            For Each c As DataGridViewColumn In Columns
                If c.Visible Then Return c.Index
            Next
            Return 0
        End Function

        Private Sub EnsureVisible(index As Integer)
            Dim visible = Math.Max(1, DisplayedRowCount(False))
            If index < FirstDisplayedScrollingRowIndex Then
                FirstDisplayedScrollingRowIndex = index
            ElseIf index >= FirstDisplayedScrollingRowIndex + visible Then
                FirstDisplayedScrollingRowIndex = Math.Max(0, index - visible + 1)
            End If
        End Sub

        ' ---------------------------------------------------------------- scrolling without scrollbars
        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            If Rows.Count = 0 Then Return
            Dim stepRows = Math.Max(1, SystemInformation.MouseWheelScrollLines \ 2)
            Dim target = FirstDisplayedScrollingRowIndex - Math.Sign(e.Delta) * stepRows
            Dim maxFirst = Math.Max(0, Rows.Count - DisplayedRowCount(False))
            FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(maxFirst, target))
            Invalidate()
        End Sub

        Protected Overrides Sub OnCurrentCellChanged(e As EventArgs)
            MyBase.OnCurrentCellChanged(e)
            If CurrentRow IsNot Nothing Then EnsureVisible(CurrentRow.Index)
            Invalidate()
        End Sub

        ' ---------------------------------------------------------------- painting
        Protected Overrides Sub OnCellPainting(e As DataGridViewCellPaintingEventArgs)
            MyBase.OnCellPainting(e)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
            Dim col = Columns(e.ColumnIndex)
            If Not _statusColumns.Contains(col.Name) Then Return
            e.PaintBackground(e.CellBounds, True)
            Dim text = Convert.ToString(e.Value)
            If text.Length > 0 Then
                Dim g = e.Graphics
                Gfx.HighQuality(g)
                Dim size = TextRenderer.MeasureText(text, Theme.LabelBold)
                Dim h = Theme.Dpi(24)
                Dim r As New Rectangle(e.CellBounds.X + Theme.Dpi(12), e.CellBounds.Y + (e.CellBounds.Height - h) \ 2, size.Width + Theme.Dpi(14), h)
                Dim solid = SolidStatuses.Contains(text)
                Using path = Gfx.RoundedRect(r, h / 2.0F)
                    If solid Then
                        Using b As New SolidBrush(Theme.Ink)
                            g.FillPath(b, path)
                        End Using
                    Else
                        Using p As New Pen(Theme.Silver)
                            g.DrawPath(p, path)
                        End Using
                    End If
                End Using
                TextRenderer.DrawText(g, text, Theme.LabelBold, r, If(solid, Theme.White, Theme.Slate),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
            End If
            e.Handled = True
        End Sub

        Protected Overrides Sub OnRowPostPaint(e As DataGridViewRowPostPaintEventArgs)
            MyBase.OnRowPostPaint(e)
            If Not Rows(e.RowIndex).Selected Then Return
            Using b As New SolidBrush(Theme.Ink)
                e.Graphics.FillRectangle(b, e.RowBounds.X, e.RowBounds.Y + Theme.Dpi(6), Theme.Dpi(3), e.RowBounds.Height - Theme.Dpi(12))
            End Using
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            If Rows.Count = 0 Then
                Dim area As New Rectangle(0, ColumnHeadersHeight, Width, Math.Max(0, Height - ColumnHeadersHeight))
                TextRenderer.DrawText(g, _emptyText, Theme.Body, area, Theme.Slate,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                Return
            End If
            ' More rows below the fold: a quiet hint instead of a scrollbar.
            Dim lastShown = FirstDisplayedScrollingRowIndex + DisplayedRowCount(False)
            If lastShown < Rows.Count Then
                Dim hint = $"{Rows.Count - lastShown} more  ·  scroll"
                Dim sz = TextRenderer.MeasureText(hint, Theme.Hint)
                Dim r As New Rectangle(Width - sz.Width - Theme.Dpi(20), Height - sz.Height - Theme.Dpi(8), sz.Width + Theme.Dpi(12), sz.Height + Theme.Dpi(4))
                Using b As New SolidBrush(Color.FromArgb(235, Theme.White))
                    g.FillRectangle(b, r)
                End Using
                TextRenderer.DrawText(g, hint, Theme.Hint, r, Theme.Slate, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End If
        End Sub

    End Class

End Namespace
