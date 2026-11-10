Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Models

Namespace UI.Controls

    ''' <summary>
    ''' Roles x permissions checkbox grid. Rows = permissions (label + what it allows), columns = roles.
    ''' The Admin column is locked to "all on". Click a cell, or use arrow keys + Space. Raises Changed on every toggle.
    ''' </summary>
    Public Class PermissionMatrix
        Inherits Control

        Private _roles As New List(Of Role)
        Private _perms As New List(Of Permission)
        Private _grants As New HashSet(Of Tuple(Of Integer, Integer))
        Private _focusRow As Integer
        Private _focusCol As Integer = 1
        Private _hover As Point = New Point(-1, -1)

        Public Event Changed As EventHandler

        ' What each module permission lets a role do (shown under the label).
        Private Shared ReadOnly Descriptions As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"DASHBOARD", "Today's services and sales, recent transactions"},
            {"ADD_SERVICE", "Bookings, downpayments, billing and payment"},
            {"REPORTS", "Sales reports, printing and verification"},
            {"SETTINGS", "Users, roles, pricing, studio and email setup"},
            {"VOID_SALE", "Cancel a completed sale (reason required)"}
        }

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            TabStop = True
            BackColor = Theme.White
            AccessibleRole = AccessibleRole.Table
            AccessibleName = "Role permissions"
        End Sub

        Public Sub Bind(roles As List(Of Role), perms As List(Of Permission), grants As HashSet(Of Tuple(Of Integer, Integer)))
            _roles = roles
            _perms = perms
            _grants = New HashSet(Of Tuple(Of Integer, Integer))(grants)
            _focusRow = 0
            _focusCol = Math.Min(1, Math.Max(0, roles.Count - 1))
            Invalidate()
        End Sub

        ''' <summary>The grants as currently ticked (Admin column included as all-on).</summary>
        Public ReadOnly Property Grants As HashSet(Of Tuple(Of Integer, Integer))
            Get
                Return New HashSet(Of Tuple(Of Integer, Integer))(_grants)
            End Get
        End Property

        ''' <summary>Rows share the height the page gives the matrix (roomy when there is space, compact when not).</summary>
        Public ReadOnly Property RowHeight As Integer
            Get
                Dim fit = (Height - HeaderHeight) \ Math.Max(1, _perms.Count)
                Return Math.Max(Theme.Dpi(44), Math.Min(Theme.Dpi(58), fit))
            End Get
        End Property

        Public ReadOnly Property HeaderHeight As Integer
            Get
                Return Theme.Dpi(50)
            End Get
        End Property

        ''' <summary>Height at the roomiest row size.</summary>
        Public ReadOnly Property PreferredHeight As Integer
            Get
                Return HeaderHeight + Theme.Dpi(58) * Math.Max(1, _perms.Count)
            End Get
        End Property

        ''' <summary>Smallest height that still shows every row.</summary>
        Public ReadOnly Property MinimumHeight As Integer
            Get
                Return HeaderHeight + Theme.Dpi(44) * Math.Max(1, _perms.Count)
            End Get
        End Property

        Private ReadOnly Property LabelWidth As Integer
            Get
                Return Math.Max(Theme.Dpi(260), Width - Theme.Dpi(150) * Math.Max(1, _roles.Count))
            End Get
        End Property

        Private Function ColumnRect(col As Integer) As Rectangle
            Dim colW = (Width - LabelWidth) \ Math.Max(1, _roles.Count)
            Return New Rectangle(LabelWidth + col * colW, 0, colW, Height)
        End Function

        Private Function CellRect(row As Integer, col As Integer) As Rectangle
            Dim c = ColumnRect(col)
            Return New Rectangle(c.X, HeaderHeight + row * RowHeight, c.Width, RowHeight)
        End Function

        Private Function IsLocked(col As Integer) As Boolean
            Return col >= 0 AndAlso col < _roles.Count AndAlso _roles(col).IsAdmin
        End Function

        Private Function IsOn(row As Integer, col As Integer) As Boolean
            Return IsLocked(col) OrElse _grants.Contains(Tuple.Create(_roles(col).Id, _perms(row).Id))
        End Function

        Private Sub Toggle(row As Integer, col As Integer)
            If row < 0 OrElse col < 0 OrElse row >= _perms.Count OrElse col >= _roles.Count OrElse IsLocked(col) OrElse Not Enabled Then Return
            Dim key = Tuple.Create(_roles(col).Id, _perms(row).Id)
            If Not _grants.Remove(key) Then _grants.Add(key)
            Invalidate()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        Private Function HitTest(p As Point) As Point
            For col = 0 To _roles.Count - 1
                For row = 0 To _perms.Count - 1
                    If CellRect(row, col).Contains(p) Then Return New Point(col, row)
                Next
            Next
            Return New Point(-1, -1)
        End Function

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim h = HitTest(e.Location)
            Cursor = If(h.X >= 0 AndAlso Not IsLocked(h.X), Cursors.Hand, Cursors.Default)
            If h <> _hover Then
                _hover = h
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = New Point(-1, -1)
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            Dim h = HitTest(e.Location)
            If h.X < 0 Then Return
            _focusCol = h.X
            _focusRow = h.Y
            Toggle(h.Y, h.X)
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData
                Case Keys.Left, Keys.Right, Keys.Up, Keys.Down : Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            Select Case e.KeyCode
                Case Keys.Left : _focusCol = Math.Max(0, _focusCol - 1)
                Case Keys.Right : _focusCol = Math.Min(_roles.Count - 1, _focusCol + 1)
                Case Keys.Up : _focusRow = Math.Max(0, _focusRow - 1)
                Case Keys.Down : _focusRow = Math.Min(_perms.Count - 1, _focusRow + 1)
                Case Keys.Space
                    Toggle(_focusRow, _focusCol)
                    e.SuppressKeyPress = True
            End Select
            Invalidate()
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
            If _roles.Count = 0 OrElse _perms.Count = 0 Then Return

            ' Header: role names (Admin marked as locked).
            TextRenderer.DrawText(g, "Module", Theme.LabelBold, New Rectangle(Theme.Dpi(4), 0, LabelWidth, HeaderHeight), Theme.Slate,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
            For col = 0 To _roles.Count - 1
                Dim c = ColumnRect(col)
                Dim head As New Rectangle(c.X, Theme.Dpi(6), c.Width, HeaderHeight \ 2)
                TextRenderer.DrawText(g, _roles(col).Name, Theme.BodyBold, head, Theme.Ink,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.Bottom Or TextFormatFlags.NoPrefix)
                Dim sub1 As New Rectangle(c.X, head.Bottom, c.Width, HeaderHeight \ 2 - Theme.Dpi(6))
                TextRenderer.DrawText(g, If(IsLocked(col), "always full access", "editable"), Theme.Hint, sub1, Theme.Slate,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.Top Or TextFormatFlags.NoPrefix)
            Next
            Using p As New Pen(Theme.Mist)
                g.DrawLine(p, 0, HeaderHeight - 1, Width, HeaderHeight - 1)
            End Using

            For row = 0 To _perms.Count - 1
                Dim top = HeaderHeight + row * RowHeight
                Dim perm = _perms(row)
                Dim desc As String = Nothing
                Descriptions.TryGetValue(perm.Key, desc)
                Dim nameLabel = perm.Label
                Dim paren = nameLabel.IndexOf(" (", StringComparison.Ordinal)
                If paren > 0 Then nameLabel = nameLabel.Substring(0, paren)
                Dim textH = Theme.BodyBold.Height + Theme.Label.Height
                Dim ty = top + (RowHeight - textH) \ 2
                TextRenderer.DrawText(g, nameLabel, Theme.BodyBold, New Rectangle(Theme.Dpi(4), ty, LabelWidth - Theme.Dpi(12), Theme.BodyBold.Height),
                                      Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
                TextRenderer.DrawText(g, If(desc, perm.Key), Theme.Label, New Rectangle(Theme.Dpi(4), ty + Theme.BodyBold.Height, LabelWidth - Theme.Dpi(12), Theme.Label.Height),
                                      Theme.Slate, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)

                For col = 0 To _roles.Count - 1
                    Dim cell = CellRect(row, col)
                    If _hover.X = col AndAlso _hover.Y = row AndAlso Not IsLocked(col) Then
                        Using b As New SolidBrush(Theme.Snow)
                            g.FillRectangle(b, cell)
                        End Using
                    End If
                    Dim s = Theme.Dpi(20.0F)
                    Dim box As New RectangleF(cell.X + (cell.Width - s) / 2, cell.Y + (cell.Height - s) / 2, s, s)
                    LsCheck.DrawBox(g, box, If(IsOn(row, col), 1.0, 0.0), Enabled AndAlso Not IsLocked(col),
                                    Focused AndAlso _focusRow = row AndAlso _focusCol = col)
                    If Focused AndAlso ShowFocusCues AndAlso _focusRow = row AndAlso _focusCol = col Then
                        Dim fr = Rectangle.Round(box)
                        fr.Inflate(Theme.Dpi(5), Theme.Dpi(5))
                        Using p As New Pen(Theme.Slate) With {.DashStyle = Drawing2D.DashStyle.Dot}
                            g.DrawRectangle(p, fr)
                        End Using
                    End If
                Next
                Using p As New Pen(Theme.Mist)
                    g.DrawLine(p, 0, top + RowHeight - 1, Width, top + RowHeight - 1)
                End Using
            Next
        End Sub

    End Class

End Namespace
