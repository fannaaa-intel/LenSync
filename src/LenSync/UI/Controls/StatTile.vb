Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports LenSync.Core

Namespace UI.Controls

    ''' <summary>
    ''' A headline number on a white card: spaced caption, large value that counts up when it changes, and a short
    ''' note underneath. Viewfinder brackets frame it; a clickable tile darkens them on hover and shows a chevron.
    ''' Keyboard: Tab to it, Enter/Space to open.
    ''' </summary>
    Public Class StatTile
        Inherits Control

        Private _value As Decimal
        Private _shown As Decimal
        Private _note As String = ""
        Private _isMoney As Boolean
        Private _clickable As Boolean
        Private _hover As Boolean
        Private _hasValue As Boolean
        Private ReadOnly _tween As New Tween()

        Public Sub New()
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.Selectable Or ControlStyles.StandardClick, True)
            TabStop = False
            BackColor = Theme.Snow
            AccessibleRole = AccessibleRole.StaticText
        End Sub

        Public Property IsMoney As Boolean
            Get
                Return _isMoney
            End Get
            Set(v As Boolean)
                _isMoney = v
                Invalidate()
            End Set
        End Property

        Public Property Note As String
            Get
                Return _note
            End Get
            Set(v As String)
                _note = If(v, "")
                Invalidate()
            End Set
        End Property

        ''' <summary>True: hand cursor, hover state, focusable; raises Click.</summary>
        Public Property Clickable As Boolean
            Get
                Return _clickable
            End Get
            Set(v As Boolean)
                _clickable = v
                TabStop = v
                Cursor = If(v, Cursors.Hand, Cursors.Default)
                AccessibleRole = If(v, AccessibleRole.PushButton, AccessibleRole.StaticText)
                Invalidate()
            End Set
        End Property

        ''' <summary>Sets the number; it counts up/down from the previous value (no animation the first time).</summary>
        Public Sub SetValue(value As Decimal)
            Dim from = _shown
            _value = value
            AccessibleName = $"{Text}: {Format(value)}"
            If Not _hasValue OrElse Not IsHandleCreated OrElse from = value Then
                _hasValue = True
                _shown = value
                Invalidate()
                Return
            End If
            _tween.Start(Theme.OpenMs,
                Sub(t)
                    _shown = from + (value - from) * CDec(Easing.OutCubic(t))
                    Invalidate()
                End Sub,
                Sub()
                    _shown = value
                    Invalidate()
                End Sub)
        End Sub

        Private Function Format(v As Decimal) As String
            If Not _hasValue Then Return "—"
            If _isMoney Then Return Money.Format(Decimal.Round(v, 2))
            Return Decimal.Round(v, 0).ToString("#,##0", CultureInfo.InvariantCulture)
        End Function

        ' ---------------------------------------------------------------- input
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

        Protected Overrides Sub OnClick(e As EventArgs)
            If _clickable Then MyBase.OnClick(e)
        End Sub

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If _clickable AndAlso (e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space) Then
                e.Handled = True
                OnClick(EventArgs.Empty)
            End If
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Return (_clickable AndAlso keyData = Keys.Enter) OrElse MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        ' ---------------------------------------------------------------- paint
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            Gfx.HighQuality(g)
            ' Focus outline only for keyboard users (Windows' focus cues), so the first tile doesn't look selected on open.
            Dim keyFocus = _clickable AndAlso Focused AndAlso ShowFocusCues
            Dim active = _clickable AndAlso (_hover OrElse keyFocus)
            Using path = Gfx.RoundedRect(New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), Theme.Dpi(CSng(Theme.Radius)))
                Using b As New SolidBrush(Theme.White)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(If(keyFocus, Theme.Ink, If(active, Theme.Silver, Theme.Mist)), If(keyFocus, Theme.Dpi(1.5F), 1.0F))
                    g.DrawPath(p, path)
                End Using
            End Using
            Dim inset = Theme.Dpi(8.0F)
            Using p As New Pen(If(active, Theme.Ink, Theme.Mist), Theme.Dpi(1.25F))
                Gfx.DrawViewfinder(g, New RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1), Theme.Dpi(10.0F), p)
            End Using

            Dim pad = Theme.Dpi(22)
            Dim w = Width - pad * 2
            Dim y = Theme.Dpi(18)
            Gfx.DrawTracked(g, If(Text, "").ToUpperInvariant(), Theme.Caption, Theme.Slate, pad, y, Theme.Dpi(2.0F))
            If _clickable Then
                Dim s = Theme.Dpi(14.0F)
                Gfx.DrawChevron(g, New RectangleF(Width - pad - s, y + (Theme.Caption.Height - s) / 2, s, s),
                                If(active, Theme.Ink, Theme.Silver), Theme.Dpi(1.5F), horizontal:=1)
            End If
            y += Theme.Caption.Height + Theme.Dpi(6)
            Const one As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding
            TextRenderer.DrawText(g, Format(_shown), Theme.TitleLarge, New Rectangle(pad - Theme.Dpi(1), y, w, Theme.TitleLarge.Height), Theme.Ink, one)
            TextRenderer.DrawText(g, _note, Theme.Hint, New Rectangle(pad, Height - Theme.Dpi(16) - Theme.Hint.Height, w, Theme.Hint.Height + 2),
                                  Theme.Slate, one)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
