Option Strict On
Option Explicit On
Option Infer On

Imports System.ComponentModel
Imports System.Drawing.Drawing2D

Namespace UI.Controls

    Public Enum ButtonVariant
        ''' <summary>Ink fill, white text. One per screen/dialog: the main action.</summary>
        Primary
        ''' <summary>White fill, silver outline. Safe/alternative actions.</summary>
        Secondary
        ''' <summary>No fill until hovered. Low-emphasis actions.</summary>
        Ghost
    End Enum

    ''' <summary>
    ''' Flat rounded monochrome button with eased hover/press states. Implements IButtonControl so it works as a
    ''' form's AcceptButton/CancelButton, and Enter clicks whichever LsButton has focus.
    ''' </summary>
    <DefaultEvent("Click")>
    Public Class LsButton
        Inherits Control
        Implements IButtonControl

        Private _variant As ButtonVariant = ButtonVariant.Primary
        Private _hoverT As Double
        Private _hoverFrom As Double
        Private _hoverTo As Double
        Private _pressed As Boolean
        Private _isDefault As Boolean
        Private _dialogResult As DialogResult = DialogResult.None
        Private ReadOnly _hoverTween As New Tween()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                     ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or
                     ControlStyles.SupportsTransparentBackColor Or ControlStyles.Selectable Or
                     ControlStyles.StandardClick, True)
            SetStyle(ControlStyles.StandardDoubleClick, False)
            Font = Theme.Button
            Cursor = Cursors.Hand
            TabStop = True
            Size = Theme.Dpi(Theme.ButtonMinWidth, Theme.ButtonHeight)
        End Sub

        Public Sub New(text As String, Optional kind As ButtonVariant = ButtonVariant.Primary)
            Me.New()
            Me.Text = text
            _variant = kind
            FitToText()
        End Sub

        <DefaultValue(ButtonVariant.Primary)>
        Public Property Kind As ButtonVariant
            Get
                Return _variant
            End Get
            Set(value As ButtonVariant)
                _variant = value
                Invalidate()
            End Set
        End Property

        ''' <summary>Widens the button to fit its text, never below Theme.ButtonMinWidth.</summary>
        Public Sub FitToText(Optional minWidth As Integer = Theme.ButtonMinWidth)
            Dim textW = TextRenderer.MeasureText(Text, Font).Width
            Width = Math.Max(Theme.Dpi(minWidth), textW + Theme.Dpi(40))
            Height = Theme.Dpi(Theme.ButtonHeight)
        End Sub

        ' ---------------------------------------------------------------- IButtonControl
        Public Property DialogResult As DialogResult Implements IButtonControl.DialogResult
            Get
                Return _dialogResult
            End Get
            Set(value As DialogResult)
                _dialogResult = value
            End Set
        End Property

        Public Sub NotifyDefault(value As Boolean) Implements IButtonControl.NotifyDefault
            If _isDefault <> value Then
                _isDefault = value
                Invalidate()
            End If
        End Sub

        Public Sub PerformClick() Implements IButtonControl.PerformClick
            If CanSelect Then OnClick(EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnClick(e As EventArgs)
            Dim f = FindForm()
            If f IsNot Nothing AndAlso _dialogResult <> DialogResult.None Then f.DialogResult = _dialogResult
            MyBase.OnClick(e)
        End Sub

        ' ---------------------------------------------------------------- interaction
        Private Sub AnimateHover(target As Double)
            _hoverFrom = _hoverT
            _hoverTo = target
            _hoverTween.Start(Theme.HoverMs,
                Sub(t)
                    _hoverT = _hoverFrom + (_hoverTo - _hoverFrom) * Easing.OutCubic(t)
                    Invalidate()
                End Sub)
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            AnimateHover(1)
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _pressed = False
            AnimateHover(0)
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Button = MouseButtons.Left Then
                _pressed = True
                Focus()
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If _pressed Then
                _pressed = False
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Space Then
                _pressed = True
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
            MyBase.OnKeyUp(e)
            If e.KeyCode = Keys.Space AndAlso _pressed Then
                _pressed = False
                Invalidate()
                PerformClick()
            End If
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            _pressed = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnEnabledChanged(e As EventArgs)
            MyBase.OnEnabledChanged(e)
            Cursor = If(Enabled, Cursors.Hand, Cursors.Default)
            Invalidate()
        End Sub

        Protected Overrides Sub OnTextChanged(e As EventArgs)
            MyBase.OnTextChanged(e)
            Invalidate()
        End Sub

        ' ---------------------------------------------------------------- painting
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim pressOffset = If(_pressed, Theme.Dpi(1), 0)
            Dim body As New RectangleF(0.5F, 0.5F, Width - 1.0F, Height - 1.0F)
            Dim radius = Theme.Dpi(CSng(Theme.RadiusSmall))

            Dim fill As Color
            Dim border As Color = Color.Empty
            Dim fore As Color
            Select Case _variant
                Case ButtonVariant.Primary
                    fill = If(_pressed, Theme.InkPressed, Theme.Blend(Theme.Ink, Theme.Charcoal, _hoverT))
                    fore = Theme.White
                Case ButtonVariant.Secondary
                    fill = If(_pressed, Theme.MistPressed, Theme.Blend(Theme.White, Theme.SnowHover, _hoverT))
                    border = Theme.Blend(Theme.Silver, Theme.Slate, _hoverT)
                    fore = Theme.Ink
                Case Else
                    fill = If(_pressed, Theme.MistPressed, Color.FromArgb(CInt(255 * _hoverT), Theme.SnowHover))
                    fore = Theme.Ink
            End Select
            If Not Enabled Then
                fill = If(_variant = ButtonVariant.Primary, Theme.Mist, Theme.White)
                border = If(_variant = ButtonVariant.Secondary, Theme.Mist, Color.Empty)
                fore = Theme.Silver
            End If

            Using path = Gfx.RoundedRect(body, radius)
                Using b As New SolidBrush(fill)
                    g.FillPath(b, path)
                End Using
                ' Keyboard focus: a heavier ink outline (secondary/ghost) or an inner white hairline (primary).
                Dim showFocus = Focused AndAlso Enabled
                If showFocus AndAlso _variant <> ButtonVariant.Primary Then border = Theme.Ink
                If border <> Color.Empty Then
                    Using p As New Pen(border, If(showFocus, Theme.Dpi(2.0F), 1.0F))
                        g.DrawPath(p, path)
                    End Using
                End If
                If showFocus AndAlso _variant = ButtonVariant.Primary Then
                    Dim inset = Theme.Dpi(3.0F)
                    Dim inner As New RectangleF(body.X + inset, body.Y + inset, body.Width - inset * 2, body.Height - inset * 2)
                    Using innerPath = Gfx.RoundedRect(inner, Math.Max(1.0F, radius - inset))
                        Using p As New Pen(Color.FromArgb(150, Theme.White), 1.0F)
                            g.DrawPath(p, innerPath)
                        End Using
                    End Using
                End If
            End Using

            Dim textRect = New Rectangle(0, pressOffset, Width, Height)
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or
                TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _hoverTween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
