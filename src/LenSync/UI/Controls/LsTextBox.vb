Option Strict On
Option Explicit On
Option Infer On

Imports System.ComponentModel

Namespace UI.Controls

    Public Enum InputFilter
        None
        Digits
        ''' <summary>Digits, one decimal point, thousands commas.</summary>
        Money
        ''' <summary>Digits, spaces, + - ( ).</summary>
        Phone
    End Enum

    ''' <summary>
    ''' Themed single-line input: rounded white field, optional leading line icon, placeholder, and for passwords
    ''' a show/hide eye. The border eases from silver to ink on focus. Tab goes straight to the inner TextBox.
    ''' </summary>
    <DefaultEvent("TextChanged")>
    Public Class LsTextBox
        Inherits Control

        Private ReadOnly _box As New TextBox()
        Private ReadOnly _eye As New EyeToggle()
        Private ReadOnly _focusTween As New Tween()
        Private _focusT As Double
        Private _hover As Boolean
        Private _placeholder As String = ""
        Private _icon As LineIcon?
        Private _isPassword As Boolean

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            Cursor = Cursors.IBeam
            Font = Theme.Body

            _box.BorderStyle = BorderStyle.None
            _box.AccessibleRole = AccessibleRole.Text
            _box.Font = Theme.Body
            _box.BackColor = Theme.White
            _box.ForeColor = Theme.Ink
            Controls.Add(_box)

            _eye.Visible = False
            AddHandler _eye.Click, AddressOf OnEyeClick
            Controls.Add(_eye)

            AddHandler _box.TextChanged, Sub(s, e) OnTextChanged(e)
            AddHandler _box.KeyDown, Sub(s, e) OnKeyDown(e)
            AddHandler _box.KeyPress, Sub(s, e)
                                          ApplyFilter(e)
                                          If Not e.Handled Then OnKeyPress(e)
                                      End Sub
            AddHandler _box.KeyUp, Sub(s, e) OnKeyUp(e)
            AddHandler _box.Enter, Sub() AnimateFocus(1)
            AddHandler _box.Leave, Sub() AnimateFocus(0)
            AddHandler _box.MouseEnter, Sub() SetHover(True)
            AddHandler _box.MouseLeave, Sub() SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)))
            AddHandler _box.HandleCreated, Sub() Native.SetCueBanner(_box, _placeholder)

            Height = Theme.Dpi(40)
        End Sub

        ' ---------------------------------------------------------------- properties
        <Browsable(True), EditorBrowsable(EditorBrowsableState.Always), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)>
        Public Overrides Property Text As String
            Get
                Return _box.Text
            End Get
            Set(value As String)
                _box.Text = value
            End Set
        End Property

        Public Property Placeholder As String
            Get
                Return _placeholder
            End Get
            Set(value As String)
                _placeholder = If(value, "")
                Native.SetCueBanner(_box, _placeholder)
            End Set
        End Property

        ''' <summary>What screen readers (and UI tests) call this field, e.g. "Username". Use the visible label text.</summary>
        Public Property FieldName As String
            Get
                Return _box.AccessibleName
            End Get
            Set(value As String)
                _box.AccessibleName = value
                AccessibleName = value
            End Set
        End Property

        ''' <summary>Masks input and shows the eye toggle.</summary>
        Public Property IsPassword As Boolean
            Get
                Return _isPassword
            End Get
            Set(value As Boolean)
                _isPassword = value
                _box.UseSystemPasswordChar = value
                _eye.Visible = value
                _eye.Revealed = False
                PerformLayout()
            End Set
        End Property

        Public Property Icon As LineIcon?
            Get
                Return _icon
            End Get
            Set(value As LineIcon?)
                _icon = value
                PerformLayout()
                Invalidate()
            End Set
        End Property

        Public Property MaxLength As Integer
            Get
                Return _box.MaxLength
            End Get
            Set(value As Integer)
                _box.MaxLength = value
            End Set
        End Property

        ''' <summary>Several lines (e.g. package inclusions). Enter adds a new line instead of submitting.</summary>
        Public Property Multiline As Boolean
            Get
                Return _box.Multiline
            End Get
            Set(value As Boolean)
                _box.Multiline = value
                _box.AcceptsReturn = value
                _box.WordWrap = True
                _box.ScrollBars = ScrollBars.None
                PerformLayout()
            End Set
        End Property

        ''' <summary>Blocks characters that can never be valid (validation still runs on save).</summary>
        Public Property Filter As InputFilter
            Get
                Return _filter
            End Get
            Set(value As InputFilter)
                _filter = value
            End Set
        End Property
        Private _filter As InputFilter = InputFilter.None

        ''' <summary>The inner TextBox (for input filters or selection).</summary>
        Public ReadOnly Property InnerTextBox As TextBox
            Get
                Return _box
            End Get
        End Property

        Public Sub SelectAll()
            _box.SelectAll()
        End Sub

        ' ---------------------------------------------------------------- focus & hover
        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            _box.Focus()
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            _box.Focus()
            _box.SelectionStart = _box.TextLength
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            SetHover(True)
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)))
        End Sub

        Private Sub SetHover(value As Boolean)
            If _hover = value Then Return
            _hover = value
            Invalidate()
        End Sub

        Private Sub AnimateFocus(target As Double)
            Dim from = _focusT
            _focusTween.Start(Theme.HoverMs,
                Sub(t)
                    _focusT = from + (target - from) * Easing.OutCubic(t)
                    Invalidate()
                End Sub)
        End Sub

        Private Sub ApplyFilter(e As KeyPressEventArgs)
            If Char.IsControl(e.KeyChar) Then Return
            Dim c = e.KeyChar
            Select Case _filter
                Case InputFilter.Digits
                    e.Handled = Not Char.IsDigit(c)
                Case InputFilter.Money
                    e.Handled = Not (Char.IsDigit(c) OrElse c = "."c OrElse c = ","c) OrElse (c = "."c AndAlso _box.Text.Contains("."))
                Case InputFilter.Phone
                    e.Handled = Not (Char.IsDigit(c) OrElse "+-() ".IndexOf(c) >= 0)
            End Select
        End Sub

        Private Sub OnEyeClick(sender As Object, e As EventArgs)
            Dim caret = _box.SelectionStart
            _eye.Revealed = Not _eye.Revealed
            _box.UseSystemPasswordChar = Not _eye.Revealed
            _box.Focus()
            _box.SelectionStart = caret
        End Sub

        Protected Overrides Sub OnEnabledChanged(e As EventArgs)
            MyBase.OnEnabledChanged(e)
            _box.Enabled = Enabled
            _box.BackColor = If(Enabled, Theme.White, Theme.Snow)
            _eye.Enabled = Enabled
            Invalidate()
        End Sub

        ' ---------------------------------------------------------------- layout & paint
        Private ReadOnly Property IconArea As Integer
            Get
                Return If(_icon.HasValue, Theme.Dpi(36), Theme.Dpi(12))
            End Get
        End Property

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            Dim eyeW = If(_isPassword, Theme.Dpi(38), Theme.Dpi(10))
            If _box.Multiline Then
                Dim pad = Theme.Dpi(9)
                _box.Bounds = New Rectangle(IconArea, pad, Math.Max(10, Width - IconArea - eyeW), Math.Max(10, Height - pad * 2))
            Else
                Dim boxH = _box.PreferredHeight
                _box.Bounds = New Rectangle(IconArea, Math.Max(0, (Height - boxH) \ 2), Math.Max(10, Width - IconArea - eyeW), boxH)
            End If
            _eye.Bounds = New Rectangle(Width - Theme.Dpi(38), Theme.Dpi(2), Theme.Dpi(36), Math.Max(0, Height - Theme.Dpi(4)))
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim r As New RectangleF(0.5F, 0.5F, Width - 1.0F, Height - 1.0F)
            Using path = Gfx.RoundedRect(r, Theme.Dpi(CSng(Theme.RadiusSmall)))
                Using b As New SolidBrush(If(Enabled, Theme.White, Theme.Snow))
                    g.FillPath(b, path)
                End Using
                Dim idle = If(_hover AndAlso Enabled, Theme.Slate, Theme.Silver)
                Dim border = If(Enabled, Theme.Blend(idle, Theme.Ink, _focusT), Theme.Mist)
                Using p As New Pen(border, 1.0F + CSng(Theme.DpiScale * 0.6 * _focusT))
                    g.DrawPath(p, path)
                End Using
            End Using
            If _icon.HasValue Then
                Dim s = Theme.Dpi(16.0F)
                Dim iconColor = If(Enabled, Theme.Blend(Theme.Silver, Theme.Ink, _focusT), Theme.Mist)
                Gfx.DrawLineIcon(g, _icon.Value, New RectangleF(Theme.Dpi(13.0F), (Height - s) / 2, s, s), iconColor, Theme.Dpi(1.5F))
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _focusTween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

        ''' <summary>The show/hide password button inside the field.</summary>
        Private NotInheritable Class EyeToggle
            Inherits Control

            Private _revealed As Boolean
            Private _hover As Boolean

            Public Sub New()
                SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                         ControlStyles.SupportsTransparentBackColor, True)
                SetStyle(ControlStyles.Selectable, False)
                TabStop = False
                Cursor = Cursors.Hand
                BackColor = Theme.White
            End Sub

            Public Property Revealed As Boolean
                Get
                    Return _revealed
                End Get
                Set(value As Boolean)
                    _revealed = value
                    AccessibleName = If(value, "Hide password", "Show password")
                    Invalidate()
                End Set
            End Property

            Protected Overrides Sub OnMouseEnter(e As EventArgs)
                MyBase.OnMouseEnter(e)
                _hover = True
                Invalidate()
            End Sub

            Protected Overrides Sub OnMouseLeave(e As EventArgs)
                MyBase.OnMouseLeave(e)
                _hover = False
                Invalidate()
            End Sub

            Protected Overrides Sub OnPaint(e As PaintEventArgs)
                Dim g = e.Graphics
                g.Clear(If(Enabled, Theme.White, Theme.Snow))
                Gfx.HighQuality(g)
                Dim s = Theme.Dpi(18.0F)
                Gfx.DrawLineIcon(g, If(_revealed, LineIcon.EyeOff, LineIcon.Eye),
                                 New RectangleF((Width - s) / 2, (Height - s) / 2, s, s),
                                 If(_hover, Theme.Ink, Theme.Slate), Theme.Dpi(1.5F))
            End Sub
        End Class

    End Class

End Namespace
