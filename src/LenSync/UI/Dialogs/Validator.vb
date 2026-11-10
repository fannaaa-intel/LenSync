Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Dialogs

    ''' <summary>
    ''' Inline field validation: a thick ink underline grows under the field, a short message slides in beneath it,
    ''' and the field shakes once. The error clears itself as soon as the user edits the field.
    ''' Layouts should leave ~22 px (96-dpi) below each validated field for the message.
    ''' </summary>
    Public NotInheritable Class Validator

        Private Shared ReadOnly _errors As New Dictionary(Of Control, FieldError)

        Private Sub New()
        End Sub

        Public Shared Sub SetError(control As Control, message As String)
            If control Is Nothing OrElse control.IsDisposed Then Return
            Dim existing As FieldError = Nothing
            If _errors.TryGetValue(control, existing) Then
                existing.Update(message)
                existing.Shake()
                Return
            End If
            Dim fe As New FieldError(control, message)
            _errors(control) = fe
            fe.Attach()
            fe.Shake()
        End Sub

        Public Shared Sub Clear(control As Control)
            If control Is Nothing Then Return
            Dim fe As FieldError = Nothing
            If _errors.TryGetValue(control, fe) Then
                _errors.Remove(control)
                fe.Detach()
            End If
        End Sub

        ''' <summary>Clears every error on <paramref name="container"/> and its descendants.</summary>
        Public Shared Sub ClearAll(container As Control)
            For Each c In ErrorsIn(container).ToList()
                Clear(c)
            Next
        End Sub

        Public Shared Function HasErrors(container As Control) As Boolean
            Return ErrorsIn(container).Any()
        End Function

        ''' <summary>Moves focus to the top-most, then left-most field with an error. Returns False if there is none.</summary>
        Public Shared Function FocusFirstError(container As Control) As Boolean
            Dim first = ErrorsIn(container).
                OrderBy(Function(c) c.PointToScreen(Point.Empty).Y).
                ThenBy(Function(c) c.PointToScreen(Point.Empty).X).
                FirstOrDefault()
            If first Is Nothing Then Return False
            first.Focus()
            Return True
        End Function

        Private Shared Function ErrorsIn(container As Control) As IEnumerable(Of Control)
            If container Is Nothing Then Return Enumerable.Empty(Of Control)()
            Return _errors.Keys.Where(Function(c) c Is container OrElse container.Contains(c)).ToList()
        End Function

    End Class

    ''' <summary>The adornments (underline + message) for one field, plus the hooks that clear them on edit.</summary>
    Friend NotInheritable Class FieldError

        Private Const GrowMs As Integer = 180
        Private Const MessageGap As Integer = 3

        Private ReadOnly _field As Control
        Private ReadOnly _host As Control
        Private ReadOnly _underline As New UnderlineBar()
        Private ReadOnly _caption As New ErrorCaption()
        Private ReadOnly _growTween As New Tween()
        Private ReadOnly _shakeTween As New Tween()
        Private _fieldRest As Point
        Private _canMoveField As Boolean
        Private _shaking As Boolean
        Private _offsetX As Integer
        Private _growT As Double = 1

        Public Sub New(field As Control, message As String)
            _field = field
            _host = FindHost(field)
            _caption.Text = If(message, "")
        End Sub

        ''' <summary>
        ''' Adornments are siblings of the field, except inside layout panels (which would grab them as cells);
        ''' there they go on the nearest free-layout ancestor.
        ''' </summary>
        Private Shared Function FindHost(field As Control) As Control
            Dim host = field.Parent
            While host IsNot Nothing AndAlso (TypeOf host Is TableLayoutPanel OrElse TypeOf host Is FlowLayoutPanel) AndAlso host.Parent IsNot Nothing
                host = host.Parent
            End While
            Return host
        End Function

        Public Sub Attach()
            If _host Is Nothing Then Return
            _canMoveField = _field.Parent Is _host AndAlso _field.Dock = DockStyle.None
            _host.Controls.Add(_underline)
            _host.Controls.Add(_caption)
            _underline.BringToFront()
            _caption.BringToFront()
            Reposition()

            AddHandler _field.TextChanged, AddressOf OnFieldEdited
            AddHandler _field.LocationChanged, AddressOf OnFieldMoved
            AddHandler _field.SizeChanged, AddressOf OnFieldMoved
            AddHandler _field.VisibleChanged, AddressOf OnFieldMoved
            AddHandler _field.Disposed, AddressOf OnFieldDisposed
            Dim combo = TryCast(_field, ComboBox)
            If combo IsNot Nothing Then AddHandler combo.SelectedIndexChanged, AddressOf OnFieldEdited
            Dim check = TryCast(_field, CheckBox)
            If check IsNot Nothing Then AddHandler check.CheckedChanged, AddressOf OnFieldEdited
            Dim radio = TryCast(_field, RadioButton)
            If radio IsNot Nothing Then AddHandler radio.CheckedChanged, AddressOf OnFieldEdited
            Dim numeric = TryCast(_field, NumericUpDown)
            If numeric IsNot Nothing Then AddHandler numeric.ValueChanged, AddressOf OnFieldEdited
            Dim picker = TryCast(_field, DateTimePicker)
            If picker IsNot Nothing Then AddHandler picker.ValueChanged, AddressOf OnFieldEdited

            ' The underline grows from the left edge; the message drops in.
            _growTween.Start(GrowMs,
                Sub(t)
                    _growT = Easing.OutCubic(t)
                    Reposition()
                End Sub)
        End Sub

        Public Sub Update(message As String)
            _caption.Text = If(message, "")
            _caption.Invalidate()
        End Sub

        Public Sub Detach()
            _growTween.Dispose()
            _shakeTween.Stop()
            If _canMoveField AndAlso _shaking AndAlso Not _field.IsDisposed Then _field.Left = _fieldRest.X
            _shakeTween.Dispose()
            RemoveHandler _field.TextChanged, AddressOf OnFieldEdited
            RemoveHandler _field.LocationChanged, AddressOf OnFieldMoved
            RemoveHandler _field.SizeChanged, AddressOf OnFieldMoved
            RemoveHandler _field.VisibleChanged, AddressOf OnFieldMoved
            RemoveHandler _field.Disposed, AddressOf OnFieldDisposed
            Dim combo = TryCast(_field, ComboBox)
            If combo IsNot Nothing Then RemoveHandler combo.SelectedIndexChanged, AddressOf OnFieldEdited
            Dim check = TryCast(_field, CheckBox)
            If check IsNot Nothing Then RemoveHandler check.CheckedChanged, AddressOf OnFieldEdited
            Dim radio = TryCast(_field, RadioButton)
            If radio IsNot Nothing Then RemoveHandler radio.CheckedChanged, AddressOf OnFieldEdited
            Dim numeric = TryCast(_field, NumericUpDown)
            If numeric IsNot Nothing Then RemoveHandler numeric.ValueChanged, AddressOf OnFieldEdited
            Dim picker = TryCast(_field, DateTimePicker)
            If picker IsNot Nothing Then RemoveHandler picker.ValueChanged, AddressOf OnFieldEdited
            If _host IsNot Nothing AndAlso Not _host.IsDisposed Then
                _host.Controls.Remove(_underline)
                _host.Controls.Remove(_caption)
            End If
            _underline.Dispose()
            _caption.Dispose()
        End Sub

        Public Sub Shake()
            If _host Is Nothing OrElse _shaking Then Return
            _shaking = True
            _fieldRest = _field.Location
            Dim amplitude = Theme.Dpi(6)
            _shakeTween.Start(Theme.ShakeMs,
                Sub(t)
                    _offsetX = CInt(amplitude * Easing.Shake(t))
                    If _canMoveField Then _field.Left = _fieldRest.X + _offsetX
                    Reposition()
                End Sub,
                Sub()
                    _offsetX = 0
                    If _canMoveField Then _field.Left = _fieldRest.X
                    _shaking = False
                    Reposition()
                End Sub)
        End Sub

        Private Sub Reposition()
            If _host Is Nothing OrElse _field.IsDisposed OrElse _underline.IsDisposed Then Return
            Dim visible = _field.Visible
            _underline.Visible = visible
            _caption.Visible = visible
            If Not visible Then Return

            ' Field bounds in host coordinates, ignoring the field's own shake offset (added explicitly below).
            Dim fieldLeft = If(_shaking AndAlso _canMoveField, _fieldRest.X, _field.Left)
            Dim r As Rectangle
            If _field.Parent Is _host Then
                r = New Rectangle(fieldLeft, _field.Top, _field.Width, _field.Height)
            Else
                r = _host.RectangleToClient(_field.Parent.RectangleToScreen(_field.Bounds))
            End If
            Dim x = r.Left + _offsetX
            Dim thick = Theme.Dpi(Theme.UnderlineThickness)
            _underline.Bounds = New Rectangle(x, r.Bottom, Math.Max(1, CInt(r.Width * _growT)), thick)

            Dim capH = _caption.PreferredHeight
            Dim maxW = Math.Max(r.Width, _host.ClientSize.Width - x - Theme.Dpi(8))
            Dim capW = Math.Min(_caption.PreferredWidth, maxW)
            Dim drop = CInt(Theme.Dpi(6) * (1 - _growT))
            _caption.Bounds = New Rectangle(x, r.Bottom + thick + Theme.Dpi(MessageGap) - drop, capW, capH)
        End Sub

        Private Sub OnFieldEdited(sender As Object, e As EventArgs)
            Validator.Clear(_field)
        End Sub

        Private Sub OnFieldMoved(sender As Object, e As EventArgs)
            ' Our own shake moves the field; only real layout changes need a reposition.
            If Not _shaking Then Reposition()
        End Sub

        Private Sub OnFieldDisposed(sender As Object, e As EventArgs)
            Validator.Clear(_field)
        End Sub

    End Class

    ''' <summary>The thick ink bar under an invalid field.</summary>
    Friend NotInheritable Class UnderlineBar
        Inherits Control

        Public Sub New()
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            BackColor = Theme.Ink
        End Sub

    End Class

    ''' <summary>Small "(!) message" line under an invalid field.</summary>
    Friend NotInheritable Class ErrorCaption
        Inherits Control

        Public Sub New()
            SetStyle(ControlStyles.Selectable, False)
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.SupportsTransparentBackColor Or ControlStyles.ResizeRedraw, True)
            TabStop = False
            Font = Theme.Hint
            BackColor = Color.Transparent
        End Sub

        Private ReadOnly Property IconSize As Integer
            Get
                Return Theme.Dpi(13)
            End Get
        End Property

        Public ReadOnly Property PreferredHeight As Integer
            Get
                Return Math.Max(IconSize, Font.Height) + Theme.Dpi(2)
            End Get
        End Property

        Public ReadOnly Property PreferredWidth As Integer
            Get
                Return IconSize + Theme.Dpi(5) +
                       TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding).Width + Theme.Dpi(4)
            End Get
        End Property

        Protected Overrides Sub OnTextChanged(e As EventArgs)
            MyBase.OnTextChanged(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim icon = IconSize
            Dim cy = Height / 2.0F
            Gfx.DrawLensIcon(g, New PointF(icon / 2.0F + 0.5F, cy), icon - 1, IconKind.Alert, 1, 1, Theme.Ink, Theme.Dpi(1.25F))
            Dim textRect As New Rectangle(icon + Theme.Dpi(5), 0, Width - icon - Theme.Dpi(5), Height)
            TextRenderer.DrawText(g, Text, Font, textRect, Theme.Ink,
                TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or
                TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
        End Sub

    End Class

End Namespace
