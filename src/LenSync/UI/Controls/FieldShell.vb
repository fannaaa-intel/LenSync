Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Controls

    ''' <summary>
    ''' The look shared by picker fields (LsSelect, LsDatePicker): the same rounded white box as LsTextBox whose border
    ''' eases from silver to ink on focus, a chevron at the right, and a popup opened by click, Space, F4 or Alt+Down.
    ''' Subclasses draw their value in PaintContent and build the popup content in CreatePopupContent.
    ''' </summary>
    Public MustInherit Class FieldShell
        Inherits Control

        Private ReadOnly _focusTween As New Tween()
        Private _focusT As Double
        Private _hover As Boolean
        Private _popup As PopupHost
        Private _closedAt As Integer

        Protected Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable Or ControlStyles.SupportsTransparentBackColor, True)
            TabStop = True
            Font = Theme.Body
            Cursor = Cursors.Hand
            Height = Theme.Dpi(40)
            AccessibleRole = AccessibleRole.ComboBox
        End Sub

        ''' <summary>What screen readers (and UI tests) call this field. Use the visible label text.</summary>
        Public Property FieldName As String
            Get
                Return AccessibleName
            End Get
            Set(value As String)
                AccessibleName = value
            End Set
        End Property

        Protected ReadOnly Property IsOpen As Boolean
            Get
                Return _popup IsNot Nothing
            End Get
        End Property

        ' ---------------------------------------------------------------- popup
        ''' <summary>The control shown in the popup (sized already). It receives focus when the popup opens.</summary>
        Protected MustOverride Function CreatePopupContent() As Control

        Public Sub OpenPopup()
            If IsOpen OrElse Not Enabled Then Return
            Dim content = CreatePopupContent()
            If content Is Nothing Then Return
            _popup = New PopupHost(content)
            AddHandler _popup.Closed, AddressOf OnPopupClosed
            AnimateFocus(1)
            _popup.Show(Me, New Point(0, Height + Theme.Dpi(4)))
            content.Focus()
        End Sub

        Protected Sub ClosePopup()
            _popup?.Close()
        End Sub

        Private Sub OnPopupClosed(sender As Object, e As ToolStripDropDownClosedEventArgs)
            Dim p = _popup
            _popup = Nothing
            _closedAt = Environment.TickCount
            If p IsNot Nothing Then
                RemoveHandler p.Closed, AddressOf OnPopupClosed
                ' Dispose after the close finishes (we're inside its event).
                BeginInvoke(New Action(Sub() p.Dispose()))
            End If
            If Not IsDisposed AndAlso CanFocus Then Focus()
            AnimateFocus(If(Focused, 1, 0))
        End Sub

        ' ---------------------------------------------------------------- input
        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Button <> MouseButtons.Left OrElse Not Enabled Then Return
            Focus()
            ' The click that closed the popup (outside it) must not reopen it at once.
            If Environment.TickCount - _closedAt < 200 Then Return
            OpenPopup()
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Return keyData = Keys.Up OrElse keyData = Keys.Down OrElse MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.F4 OrElse e.KeyCode = Keys.Space OrElse (e.Alt AndAlso e.KeyCode = Keys.Down) Then
                e.Handled = True
                OpenPopup()
            ElseIf e.KeyCode = Keys.Up OrElse e.KeyCode = Keys.Down Then
                e.Handled = True
                StepValue(If(e.KeyCode = Keys.Up, -1, 1))
            End If
        End Sub

        ''' <summary>Up/Down without opening the popup (previous/next item or day).</summary>
        Protected Overridable Sub StepValue(direction As Integer)
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            AnimateFocus(1)
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            If Not IsOpen Then AnimateFocus(0)
        End Sub

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

        Protected Overrides Sub OnEnabledChanged(e As EventArgs)
            MyBase.OnEnabledChanged(e)
            Cursor = If(Enabled, Cursors.Hand, Cursors.Default)
            Invalidate()
        End Sub

        Private Sub AnimateFocus(target As Double)
            Dim from = _focusT
            If Not IsHandleCreated Then
                _focusT = target
                Return
            End If
            _focusTween.Start(Theme.HoverMs,
                Sub(t)
                    _focusT = from + (target - from) * Easing.OutCubic(t)
                    Invalidate()
                End Sub)
        End Sub

        ' ---------------------------------------------------------------- paint
        Protected ReadOnly Property ChevronArea As Integer
            Get
                Return Theme.Dpi(36)
            End Get
        End Property

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
            Dim s = Theme.Dpi(16.0F)
            Gfx.DrawChevron(g, New RectangleF(Width - ChevronArea + (ChevronArea - s) / 2 - Theme.Dpi(2), (Height - s) / 2, s, s),
                            If(Enabled, Theme.Blend(Theme.Slate, Theme.Ink, _focusT), Theme.Mist), Theme.Dpi(1.5F), up:=IsOpen)
            PaintContent(g, New Rectangle(Theme.Dpi(12), 0, Math.Max(0, Width - Theme.Dpi(12) - ChevronArea), Height))
        End Sub

        ''' <summary>Draws the value inside <paramref name="area"/> (left padding and chevron already excluded).</summary>
        Protected MustOverride Sub PaintContent(g As Graphics, area As Rectangle)

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _focusTween.Dispose()
                _popup?.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

    ''' <summary>
    ''' A borderless drop-down that hosts one control (a list or a calendar). Closes on a click outside or Esc and
    ''' drops a soft shadow. The hosted control paints its own border.
    ''' </summary>
    Friend NotInheritable Class PopupHost
        Inherits ToolStripDropDown

        Public Sub New(content As Control)
            AutoSize = False
            Padding = Padding.Empty
            Margin = Padding.Empty
            DropShadowEnabled = True
            Renderer = New PlainRenderer()
            BackColor = Theme.White
            Dim host As New ToolStripControlHost(content) With {
                .AutoSize = False, .Margin = Padding.Empty, .Padding = Padding.Empty, .Size = content.Size}
            Items.Add(host)
            Size = content.Size
        End Sub

        ''' <summary>Paints nothing: the hosted control draws the popup's border.</summary>
        Private NotInheritable Class PlainRenderer
            Inherits ToolStripRenderer
        End Class

    End Class

End Namespace
